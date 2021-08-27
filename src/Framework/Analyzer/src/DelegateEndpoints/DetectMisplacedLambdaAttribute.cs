// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.AspNetCore.Analyzers.DelegateEndpoints;

public partial class DelegateEndpointAnalyzer : DiagnosticAnalyzer
{
    private static void DetectMisplacedLambdaAttribute(
        in OperationAnalysisContext context,
        IInvocationOperation invocation,
        IAnonymousFunctionOperation lambda)
    {
        // This analyzer will only process invocations that are immediate children of the
        // AnonymousFunctionOperation provided as the delegate endpoint. We'll support checking
        // for misplaced attributes in `() => Hello()` and `() => { return Hello(); }` but not in:
        // () => {
        //    Hello();
        //    return "foo";
        // }
        InvocationExpressionSyntax? targetInvocation = null;

        // () => Hello() has a single child which is a BlockOperation so we check to see if
        // expression associated with that operation is an invocation expression
        if (lambda.Children.FirstOrDefault().Syntax is InvocationExpressionSyntax innerInvocation)
        {
            targetInvocation = innerInvocation;
        }

        if (lambda.Children.FirstOrDefault().Children.FirstOrDefault() is IReturnOperation returnOperation
            && returnOperation.ReturnedValue is IInvocationOperation returnedInvocation)
        {
            targetInvocation = (InvocationExpressionSyntax)returnedInvocation.Syntax;
        }

        if (targetInvocation is null)
        {
            return;
        }

        var methodOperation = invocation.SemanticModel.GetSymbolInfo(targetInvocation);
        var methodSymbol = methodOperation.Symbol ?? methodOperation.CandidateSymbols.FirstOrDefault();

        // If no method definition was found for the lambda, then abort.
        if (methodSymbol is null)
        {
            return;
        }

        var attributes = methodSymbol.GetAttributes();
        var location = lambda.Syntax.GetLocation();

        foreach (var attribute in attributes)
        {
            if (IsInValidNamespace(attribute))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DetectMisplacedLambdaAttribute,
                    location,
                    attribute.AttributeClass?.Name,
                    methodSymbol.Name));
            }
        }

        bool IsInValidNamespace(AttributeData attribute)
        {
            var supportedNamespace = "Microsoft.AspNetCore";
            var symbolDisplayFormat = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

            if (attribute.AttributeClass is null)
            {
                return false;
            }

            var fullyQualifiedName = attribute.AttributeClass.ContainingNamespace.ToDisplayString(symbolDisplayFormat);
            return fullyQualifiedName.StartsWith(supportedNamespace, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}