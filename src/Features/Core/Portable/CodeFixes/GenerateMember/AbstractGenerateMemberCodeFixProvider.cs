﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CodeFixes.GenerateMember
{
#pragma warning disable RS1016 // Code fix providers should provide FixAll support. https://github.com/dotnet/roslyn/issues/23528
    internal abstract class AbstractGenerateMemberCodeFixProvider : CodeFixProvider
#pragma warning restore RS1016 // Code fix providers should provide FixAll support.
    {
        protected abstract Task<ImmutableArray<CodeAction>> GetCodeActionsAsync(Document document, SyntaxNode node, CancellationToken cancellationToken);
        protected abstract bool IsCandidate(SyntaxNode node, SyntaxToken token, Diagnostic diagnostic);

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            // TODO: https://github.com/dotnet/roslyn/issues/5777
            // Not supported in REPL for now.
            if (context.Project.IsSubmission)
            {
                return;
            }

            var diagnostic = context.Diagnostics.First();
            var document = context.Document;
            var syntaxFacts = document.GetLanguageService<ISyntaxFactsService>();

            var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var names = GetTargetNodes(syntaxFacts, root, context.Span, diagnostic);
            foreach (var name in names)
            {
                var codeActions = await GetCodeActionsAsync(context.Document, name, context.CancellationToken).ConfigureAwait(false);
                if (codeActions.IsDefaultOrEmpty)
                {
                    continue;
                }

                context.RegisterFixes(codeActions, context.Diagnostics);
                return;
            }
        }

        protected virtual SyntaxNode GetTargetNode(SyntaxNode node)
            => node;

        private IEnumerable<SyntaxNode> GetTargetNodes(
            ISyntaxFactsService syntaxFacts, SyntaxNode root,
            TextSpan span, Diagnostic diagnostic)
        {
            var token = root.FindToken(span.Start);
            if (token.Span.IntersectsWith(span))
            {
                foreach (var ancestor in token.GetAncestors<SyntaxNode>())
                {
                    // If we're crossing a local function/lambda point then stop looking higher.
                    // We've clearly gone past the point of the original diagnostic and should
                    // not consider this node as something to consider.
                    //
                    // Note: it's ok if we are on a lambda that was the direct node with the 
                    // diagnostic (i.e. if the compiler was reporting a diagnostic on a lambda
                    // itself).  However, once we start walking upwards, we don't want to cross
                    // a lambda.
                    if (syntaxFacts.IsAnonymousOrLocalFunctionStatement(ancestor) && 
                        ancestor.SpanStart < token.SpanStart)
                    {
                        break;
                    }

                    if (!IsCandidate(ancestor, token, diagnostic))
                    {
                        continue;
                    }

                    var name = GetTargetNode(ancestor);

                    if (name != null)
                    {
                        yield return name;
                    }
                }
            }
        }
    }
}
