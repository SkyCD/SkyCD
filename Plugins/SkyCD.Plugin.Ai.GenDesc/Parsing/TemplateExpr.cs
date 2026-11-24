using System;
using System.Collections.Generic;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Parsing;

/// <summary>A compiled expression in a template block, evaluated against the imported facts.</summary>
internal abstract class TemplateExpr
{
    public abstract TemplateValue Eval(DescriptionFacts facts);

    public sealed class Literal(TemplateValue value) : TemplateExpr
    {
        public override TemplateValue Eval(DescriptionFacts facts) => value;
    }

    public sealed class Variable(string name) : TemplateExpr
    {
        public override TemplateValue Eval(DescriptionFacts facts) => TemplateValue.Text(facts.Get(name));
    }

    public sealed class Call(string name, IReadOnlyList<TemplateExpr> args) : TemplateExpr
    {
        public override TemplateValue Eval(DescriptionFacts facts)
        {
            var values = new TemplateValue[args.Count];
            for (var i = 0; i < args.Count; i++) values[i] = args[i].Eval(facts);
            return TemplateFunctions.Invoke(name, values, facts);
        }
    }

    public sealed class Unary(string op, TemplateExpr operand) : TemplateExpr
    {
        public override TemplateValue Eval(DescriptionFacts facts)
        {
            var value = operand.Eval(facts);
            return op switch
            {
                "not" => TemplateValue.Bool(!value.IsTruthy()),
                "-" => TemplateValue.Number(-value.AsNumber()),
                _ => throw new InvalidOperationException($"unsupported unary operator '{op}'")
            };
        }
    }

    public sealed class Binary(string op, TemplateExpr left, TemplateExpr right) : TemplateExpr
    {
        public override TemplateValue Eval(DescriptionFacts facts)
        {
            switch (op)
            {
                case "and": return TemplateValue.Bool(left.Eval(facts).IsTruthy() && right.Eval(facts).IsTruthy());
                case "or": return TemplateValue.Bool(left.Eval(facts).IsTruthy() || right.Eval(facts).IsTruthy());
            }

            var a = left.Eval(facts);
            var b = right.Eval(facts);
            switch (op)
            {
                case "+": return Add(a, b);
                case "-": return TemplateValue.Number(a.AsNumber() - b.AsNumber());
                case "==": return TemplateValue.Bool(Compare(a, b) == 0);
                case "!=": return TemplateValue.Bool(Compare(a, b) != 0);
                case "<": return TemplateValue.Bool(Compare(a, b) < 0);
                case ">": return TemplateValue.Bool(Compare(a, b) > 0);
                case "<=": return TemplateValue.Bool(Compare(a, b) <= 0);
                case ">=": return TemplateValue.Bool(Compare(a, b) >= 0);
                default: throw new InvalidOperationException($"unsupported binary operator '{op}'");
            }
        }

        private static TemplateValue Add(TemplateValue a, TemplateValue b)
        {
            if (!a.IsTextual && !b.IsTextual) return TemplateValue.Number(a.AsNumber() + b.AsNumber());
            return TemplateValue.Text(a.Display() + b.Display());
        }

        private static int Compare(TemplateValue a, TemplateValue b)
        {
            if (a.IsTextual || b.IsTextual)
                return string.CompareOrdinal(a.Display(), b.Display());
            var x = a.AsNumber();
            var y = b.AsNumber();
            return x.CompareTo(y);
        }
    }
}
