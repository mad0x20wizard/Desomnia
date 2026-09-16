using System.ComponentModel;
using System.Globalization;

namespace MadWizard.Desomnia.Configuration
{
    /// <summary>
    /// A boolean formula over named watch metrics. A leading <c>and</c>/<c>or</c> is retained as
    /// composition metadata until this value is merged with an earlier expression. The default
    /// value has no AST and is the internal, terminal <see cref="Yield"/> expression.
    /// </summary>
    [TypeConverter(typeof(WatchExpressionConverter))]
    public readonly record struct WatchExpression
    {
        public static readonly WatchExpression Yield        = default;

        public static readonly WatchExpression DefaultAND   = new(new CatchAllNode(Operator.AND), isDefault: true);
        public static readonly WatchExpression DefaultOR    = new(new CatchAllNode(Operator.OR), isDefault: true);

        private readonly Node? _root;

        public WatchExpression(string value)
        {
            (_root, MergeOperator, IsDefault) = new Parser(value).Parse();
        }

        private WatchExpression(Node? root, Operator? merge = null, bool isDefault = false)
        {
            _root = root;

            MergeOperator = merge;

            IsDefault = isDefault;
        }

        public bool IsDefault { get; }

        public bool IsYield => _root is null;

        internal Operator? MergeOperator { get; init; }

        /// <summary>
        /// Merges two WatchExpressions.
        /// 
        /// When the right operand does not have a <see cref="MergeOperator"/> set,
        /// it will overwrite the left operand completely
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">If merge is not possible (e.g. <see cref="Yield"/> + XYZ)</exception>
        public static WatchExpression operator <<(WatchExpression left, WatchExpression right)
        {
            if (right.IsYield)
                return Yield;
            if (left.IsYield)
                throw new InvalidOperationException("A yielded watch expression is final and cannot be changed or merged.");

            if (right.IsDefault)
            {
                if (!left.IsDefault)
                    return left;

                return right.MergeOperator is Operator defaultOperator
                    ? new(new BinaryNode(left._root!, defaultOperator, right._root!), isDefault: true)
                    : right with { MergeOperator = null };
            }

            if (right.MergeOperator is not Operator mergeOperator)
                return right with { MergeOperator = null };

            if (left.IsDefault)
                return right with { MergeOperator = null };

            return new(new BinaryNode(left._root!, mergeOperator, right._root!));
        }

        public bool Evaluate(IReadOnlyDictionary<string, bool> metrics)
        {
            if (_root is null)
                throw new InvalidOperationException("Yield has no boolean value and cannot be evaluated.");

            Dictionary<string, bool> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in metrics)
                if (!values.TryAdd(name, value))
                    throw new InvalidOperationException($"Metric '{name}' was supplied more than once.");

            return _root.Evaluate(values);
        }

        public override string ToString()
        {
            if (_root is null)
                return "YIELD";

            var prefix = (IsDefault ? "default " : string.Empty) +
                MergeOperator switch
                {
                    Operator.AND    => "and ",
                    Operator.OR     => "or ",
                    _               => ""
                };

            return prefix + _root;
        }

        internal enum Operator { AND, OR }

        #region AST Nodes
        private abstract record Node
        {
            internal abstract bool Evaluate(IReadOnlyDictionary<string, bool> metrics);
        }

        private sealed record LiteralNode(bool Value) : Node
        {
            internal override bool Evaluate(IReadOnlyDictionary<string, bool> metrics) => Value;
            public override string ToString() => Value ? "true" : "false";
        }

        private sealed record SymbolNode(string Name) : Node
        {
            internal override bool Evaluate(IReadOnlyDictionary<string, bool> metrics)
            {
                if (!metrics.TryGetValue(Name, out var value))
                    throw new InvalidOperationException($"Watch expression references unknown metric '{Name}'.");
                return value;
            }
            public override string ToString() => Name;
        }

        private sealed record CatchAllNode(Operator Operator) : Node
        {
            internal override bool Evaluate(IReadOnlyDictionary<string, bool> metrics)
            {
                if (metrics.Count == 0)
                    return true;

                var result = Operator == Operator.AND;
                foreach (var value in metrics.Values)
                    result = Operator == Operator.AND ? result & value : result | value;
                return result;
            }
            public override string ToString() => Operator == Operator.AND ? "AND" : "OR";
        }

        private sealed record BinaryNode(Node Left, Operator Operator, Node Right) : Node
        {
            internal override bool Evaluate(IReadOnlyDictionary<string, bool> metrics)
            {
                var left = Left.Evaluate(metrics);
                var right = Right.Evaluate(metrics);
                return Operator == Operator.AND ? left & right : left | right;
            }
            public override string ToString() => $"({Left} {(Operator == Operator.AND ? "and" : "or")} {Right})";
        }
        #endregion

        private sealed class Parser
        {
            private readonly List<Token> _tokens;
            private int _position;

            internal Parser(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new FormatException("A watch expression cannot be empty.");
                _tokens = Tokenize(value);
            }

            internal (Node Root, Operator? MergeOperator, bool IsDefault) Parse()
            {
                var isDefault = Match(TokenKind.Default);
                if (Current.Kind == TokenKind.End)
                    throw Error("Expected a boolean formula after 'default'.");

                Operator? merge = null;
                Node root;

                if (Current.Kind is TokenKind.And or TokenKind.Or)
                {
                    var op = Current.Kind == TokenKind.And ? Operator.AND : Operator.OR;
                    Advance();

                    if (Current.Kind == TokenKind.End)
                        root = new CatchAllNode(op);
                    else
                    {
                        merge = op;
                        root = ParseOr();
                    }
                }
                else
                    root = ParseOr();

                if (Current.Kind != TokenKind.End)
                    throw Error($"Unexpected token '{Current.Text}'.");

                return new(root, merge, isDefault);
            }

            private Node ParseOr()
            {
                var left = ParseAnd();
                while (Match(TokenKind.Or))
                    left = new BinaryNode(left, Operator.OR, ParseAnd());
                return left;
            }

            private Node ParseAnd()
            {
                var left = ParsePrimary();
                while (Match(TokenKind.And))
                    left = new BinaryNode(left, Operator.AND, ParsePrimary());
                return left;
            }

            private Node ParsePrimary()
            {
                if (Match(TokenKind.True))
                    return new LiteralNode(true);
                if (Match(TokenKind.False))
                    return new LiteralNode(false);
                if (Current.Kind == TokenKind.Symbol)
                    return new SymbolNode(Advance().Text);
                if (Match(TokenKind.LeftParenthesis))
                {
                    var expression = ParseOr();
                    if (!Match(TokenKind.RightParenthesis))
                        throw Error("Expected ')'.");
                    return expression;
                }

                throw Error($"Expected a metric, literal, or parenthesized expression instead of '{Current.Text}'.");
            }

            private Token Current => _tokens[_position];
            private Token Advance() => _tokens[_position++];

            private bool Match(TokenKind kind)
            {
                if (Current.Kind != kind)
                    return false;
                _position++;
                return true;
            }

            private FormatException Error(string message) =>
                new($"Invalid watch expression at position {Current.Position}: {message}");

            private static List<Token> Tokenize(string value)
            {
                List<Token> tokens = [];
                for (var index = 0; index < value.Length;)
                {
                    if (char.IsWhiteSpace(value[index]))
                    {
                        index++;
                        continue;
                    }
                    if (value[index] == '(')
                    {
                        tokens.Add(new(TokenKind.LeftParenthesis, "(", index++));
                        continue;
                    }
                    if (value[index] == ')')
                    {
                        tokens.Add(new(TokenKind.RightParenthesis, ")", index++));
                        continue;
                    }

                    var start = index;
                    while (index < value.Length && !char.IsWhiteSpace(value[index]) && value[index] is not '(' and not ')')
                        index++;

                    var text = value[start..index];
                    var kind = text.ToUpperInvariant() switch
                    {
                        "AND" => TokenKind.And,
                        "OR" => TokenKind.Or,
                        "TRUE" => TokenKind.True,
                        "FALSE" => TokenKind.False,
                        "DEFAULT" => TokenKind.Default,
                        _ => TokenKind.Symbol,
                    };
                    tokens.Add(new(kind, text, start));
                }

                tokens.Add(new(TokenKind.End, "end of expression", value.Length));
                return tokens;
            }

            private readonly record struct Token(TokenKind Kind, string Text, int Position);
            private enum TokenKind { Symbol, And, Or, True, False, Default, LeftParenthesis, RightParenthesis, End }
        }
    }

    public sealed class WatchExpressionConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            value is string expression ? new WatchExpression(expression) : base.ConvertFrom(context, culture, value);
    }
}
