using System;
using System.Collections.Generic;
using System.Text;

namespace Wagenheimer.BuildPipeline.Editor
{
    /// <summary>
    /// Parses and evaluates a C# preprocessor condition (<c>A &amp;&amp; !B || (C == true)</c>) against a
    /// symbol set. Unknown symbols are false, exactly like the compiler. Used to say which <c>#if</c>
    /// blocks a profile turns on or off.
    /// </summary>
    public static class DefineExpression
    {
        /// <summary>Identifiers referenced by the expression, in order, without duplicates and without true/false.</summary>
        public static List<string> Symbols(string expression)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var token in Tokenize(expression))
            {
                if (token.Kind == TokenKind.Identifier && seen.Add(token.Text))
                    result.Add(token.Text);
            }

            return result;
        }

        /// <summary>Evaluates the expression; returns false for an empty/malformed expression.</summary>
        public static bool Evaluate(string expression, ISet<string> defined)
        {
            return TryEvaluate(expression, defined, out bool value) && value;
        }

        public static bool TryEvaluate(string expression, ISet<string> defined, out bool value)
        {
            value = false;
            var tokens = Tokenize(expression);
            if (tokens.Count == 0)
                return false;

            var parser = new Parser(tokens, defined ?? new HashSet<string>());
            try
            {
                value = parser.ParseOr();
                return parser.AtEnd;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private enum TokenKind { Identifier, True, False, Not, And, Or, Equal, NotEqual, Open, Close }

        private readonly struct Token
        {
            public readonly TokenKind Kind;
            public readonly string Text;

            public Token(TokenKind kind, string text = null)
            {
                Kind = kind;
                Text = text;
            }
        }

        private static List<Token> Tokenize(string expression)
        {
            var tokens = new List<Token>();
            if (string.IsNullOrEmpty(expression))
                return tokens;

            int i = 0;
            while (i < expression.Length)
            {
                char c = expression[i];

                if (char.IsWhiteSpace(c)) { i++; continue; }

                // Trailing comment ends the directive.
                if (c == '/' && i + 1 < expression.Length && expression[i + 1] == '/')
                    break;

                if (c == '(') { tokens.Add(new Token(TokenKind.Open)); i++; continue; }
                if (c == ')') { tokens.Add(new Token(TokenKind.Close)); i++; continue; }

                if (c == '&' && Peek(expression, i + 1) == '&') { tokens.Add(new Token(TokenKind.And)); i += 2; continue; }
                if (c == '|' && Peek(expression, i + 1) == '|') { tokens.Add(new Token(TokenKind.Or)); i += 2; continue; }
                if (c == '=' && Peek(expression, i + 1) == '=') { tokens.Add(new Token(TokenKind.Equal)); i += 2; continue; }
                if (c == '!' && Peek(expression, i + 1) == '=') { tokens.Add(new Token(TokenKind.NotEqual)); i += 2; continue; }
                if (c == '!') { tokens.Add(new Token(TokenKind.Not)); i++; continue; }

                if (char.IsLetter(c) || c == '_')
                {
                    var builder = new StringBuilder();
                    while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_'))
                        builder.Append(expression[i++]);

                    string word = builder.ToString();
                    if (word == "true") tokens.Add(new Token(TokenKind.True));
                    else if (word == "false") tokens.Add(new Token(TokenKind.False));
                    else tokens.Add(new Token(TokenKind.Identifier, word));
                    continue;
                }

                // Anything else is not valid preprocessor syntax; skip it so Symbols() stays useful.
                i++;
            }

            return tokens;
        }

        private static char Peek(string text, int index) => index < text.Length ? text[index] : '\0';

        private sealed class Parser
        {
            private readonly List<Token> _tokens;
            private readonly ISet<string> _defined;
            private int _position;

            public Parser(List<Token> tokens, ISet<string> defined)
            {
                _tokens = tokens;
                _defined = defined;
            }

            public bool AtEnd => _position >= _tokens.Count;

            public bool ParseOr()
            {
                bool left = ParseAnd();
                while (Accept(TokenKind.Or))
                {
                    bool right = ParseAnd();
                    left = left || right;
                }

                return left;
            }

            private bool ParseAnd()
            {
                bool left = ParseEquality();
                while (Accept(TokenKind.And))
                {
                    bool right = ParseEquality();
                    left = left && right;
                }

                return left;
            }

            private bool ParseEquality()
            {
                bool left = ParseUnary();
                while (true)
                {
                    if (Accept(TokenKind.Equal)) left = left == ParseUnary();
                    else if (Accept(TokenKind.NotEqual)) left = left != ParseUnary();
                    else return left;
                }
            }

            private bool ParseUnary()
            {
                if (Accept(TokenKind.Not))
                    return !ParseUnary();

                return ParsePrimary();
            }

            private bool ParsePrimary()
            {
                if (AtEnd)
                    throw new FormatException("Unexpected end of expression.");

                var token = _tokens[_position++];
                switch (token.Kind)
                {
                    case TokenKind.True: return true;
                    case TokenKind.False: return false;
                    case TokenKind.Identifier: return _defined.Contains(token.Text);
                    case TokenKind.Open:
                        bool inner = ParseOr();
                        if (!Accept(TokenKind.Close))
                            throw new FormatException("Missing ')'.");
                        return inner;
                    default:
                        throw new FormatException("Unexpected token.");
                }
            }

            private bool Accept(TokenKind kind)
            {
                if (AtEnd || _tokens[_position].Kind != kind)
                    return false;

                _position++;
                return true;
            }
        }
    }
}
