// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Bicep.Core.Parsing;
using Bicep.Core.Text;

namespace Bicep.Core.Syntax
{
    public class IntegerLiteralSyntax : ExpressionSyntax
    {
        public IntegerLiteralSyntax(Token literal, ulong value)
        {
            Literal = literal;
            Value = value;
        }

        public Token Literal { get; }

        public ulong Value { get; }

        public override void Accept(ISyntaxVisitor visitor)
            => visitor.VisitIntegerLiteralSyntax(this);

        public override TextSpan Span
            => TextSpan.Between(Literal, Literal);
    }
}
