using System.Globalization;
using System.Text.RegularExpressions;

namespace Halo.Core.Search;

/// <summary>
/// Answers arithmetic locally so "what is 18% of 2400" never needs a network round trip.
/// Supports + - * / ^ %, parentheses, "x"/"×"/"÷", "of", pi, e and common functions.
/// </summary>
public static class Calculator
{
    static readonly Regex Prefix = new(@"^\s*(?:what\s*is|what's|whats|calculate|compute|solve|evaluate)\s+", RegexOptions.IgnoreCase);
    static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
        { "sqrt", "sin", "cos", "tan", "log", "ln", "abs", "round", "floor", "ceil" };

    public static bool TryEvaluate(string input, out double result, out string expression)
    {
        result = 0;
        expression = "";
        if (string.IsNullOrWhiteSpace(input)) return false;

        var text = Prefix.Replace(input, "").Trim().TrimEnd('?', '=', '.').Trim();
        text = text.Replace("×", "*").Replace("÷", "/").Replace("−", "-");
        text = Regex.Replace(text, @"(?<=\d)\s*[xX]\s*(?=[\d(])", "*");
        text = Regex.Replace(text, @"\btimes\b", "*", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bplus\b", "+", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bminus\b", "-", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bdivided\s+by\b", "/", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bof\b", "*", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?<=\d),(?=\d{3}\b)", "");

        if (!Regex.IsMatch(text, @"\d")) return false;
        if (!Regex.IsMatch(text, @"[+\-*/^%]|\b(?:sqrt|sin|cos|tan|log|ln|abs|round|floor|ceil)\b", RegexOptions.IgnoreCase)) return false;

        try
        {
            var parser = new Parser(text);
            var value = parser.ParseExpression();
            if (!parser.AtEnd || double.IsNaN(value) || double.IsInfinity(value)) return false;
            result = value;
            expression = text;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The question as the user wrote it, minus "what is" and trailing "?"/"=", for display.</summary>
    public static string DisplayForm(string input) => Prefix.Replace(input ?? "", "").Trim().TrimEnd('?', '=', '.').Trim();

    public static string Format(double value)
    {
        if (Math.Abs(value) >= 1e15 || (Math.Abs(value) < 1e-6 && value != 0))
            return value.ToString("G10", CultureInfo.InvariantCulture);
        var rounded = Math.Round(value, 10);
        return rounded % 1 == 0
            ? rounded.ToString("#,0", CultureInfo.InvariantCulture)
            : rounded.ToString("#,0.##########", CultureInfo.InvariantCulture);
    }

    sealed class Parser
    {
        readonly string _s;
        int _i;

        public Parser(string s) => _s = s;

        public bool AtEnd
        {
            get { SkipSpace(); return _i >= _s.Length; }
        }

        public double ParseExpression()
        {
            var v = ParseTerm();
            while (true)
            {
                if (Eat('+')) v += ParseTerm();
                else if (Eat('-')) v -= ParseTerm();
                else return v;
            }
        }

        double ParseTerm()
        {
            var v = ParseFactor();
            while (true)
            {
                if (Eat('*')) v *= ParseFactor();
                else if (Eat('/')) v /= ParseFactor();
                else if (Peek('(')) v *= ParseFactor(); // implicit multiply: 2(3+4)
                else return v;
            }
        }

        double ParseFactor()
        {
            var v = ParseUnary();
            if (Eat('^')) v = Math.Pow(v, ParseFactor()); // right associative
            return v;
        }

        double ParseUnary()
        {
            if (Eat('-')) return -ParseUnary();
            if (Eat('+')) return ParseUnary();
            return ParsePostfix();
        }

        double ParsePostfix()
        {
            var v = ParsePrimary();
            while (Eat('%')) v /= 100;
            return v;
        }

        double ParsePrimary()
        {
            SkipSpace();
            if (Eat('('))
            {
                var v = ParseExpression();
                if (!Eat(')')) throw new FormatException("missing )");
                return v;
            }

            if (_i < _s.Length && char.IsLetter(_s[_i]))
            {
                int start = _i;
                while (_i < _s.Length && char.IsLetter(_s[_i])) _i++;
                var name = _s[start.._i].ToLowerInvariant();
                if (name == "pi") return Math.PI;
                if (name == "e") return Math.E;
                if (!Functions.Contains(name)) throw new FormatException(name);
                var arg = ParseUnary();
                return name switch
                {
                    "sqrt" => Math.Sqrt(arg),
                    "sin" => Math.Sin(arg),
                    "cos" => Math.Cos(arg),
                    "tan" => Math.Tan(arg),
                    "log" => Math.Log10(arg),
                    "ln" => Math.Log(arg),
                    "abs" => Math.Abs(arg),
                    "round" => Math.Round(arg),
                    "floor" => Math.Floor(arg),
                    _ => Math.Ceiling(arg),
                };
            }

            int numStart = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
            if (numStart == _i) throw new FormatException("number expected");
            return double.Parse(_s[numStart.._i], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        bool Eat(char c)
        {
            SkipSpace();
            if (_i < _s.Length && _s[_i] == c)
            {
                _i++;
                return true;
            }
            return false;
        }

        bool Peek(char c)
        {
            SkipSpace();
            return _i < _s.Length && _s[_i] == c;
        }

        void SkipSpace()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }
    }
}
