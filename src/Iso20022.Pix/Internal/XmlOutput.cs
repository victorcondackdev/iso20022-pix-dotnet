using System.Text;
using Iso20022.Pix.Components;

namespace Iso20022.Pix.Internal;

/// <summary>
/// A minimal XML emitter whose output is byte-identical to Go's
/// <c>encoding/xml</c> encoder with <c>Indent("", "  ")</c>, preceded by
/// <c>xml.Header</c> and followed by a newline:
/// <list type="bullet">
/// <item>an element without child elements is written on one line, <c>&lt;X&gt;text&lt;/X&gt;</c> or <c>&lt;X&gt;&lt;/X&gt;</c> (never self-closing);</item>
/// <item>text and attribute values escape <c>&amp; &lt; &gt; " '</c>, tab, newline and carriage return as character references;</item>
/// <item>code points that XML 1.0 cannot carry are replaced by U+FFFD.</item>
/// </list>
/// <see cref="System.Xml.XmlWriter"/> differs in each of these details, which is
/// why it is not used here.
/// </summary>
internal sealed class XmlOutput
{
    private const string Header = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";

    private readonly StringBuilder _b = new(2048);
    private int _depth;
    private bool _indentedIn;
    private bool _putNewline;

    public XmlOutput()
    {
        _b.Append(Header);
    }

    public void Start(string name, string? ns = null)
    {
        WriteIndent(1);
        _b.Append('<').Append(name);
        if (!string.IsNullOrEmpty(ns))
        {
            _b.Append(" xmlns=\"");
            Escape(ns);
            _b.Append('"');
        }

        _b.Append('>');
    }

    public void End(string name)
    {
        WriteIndent(-1);
        _b.Append("</").Append(name).Append('>');
    }

    /// <summary>An element with text content, always written.</summary>
    public void Element(string name, string? value)
    {
        WriteIndent(1);
        _b.Append('<').Append(name).Append('>');
        Escape(value);
        End(name);
    }

    /// <summary>An element written only when <paramref name="value"/> is not empty (<c>omitempty</c>).</summary>
    public void Optional(string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            Element(name, value);
        }
    }

    /// <summary>A list of text elements; each is written, even when empty.</summary>
    public void Elements(string name, List<string> values)
    {
        foreach (string v in values)
        {
            Element(name, v);
        }
    }

    /// <summary>
    /// A list of text elements declared <c>omitempty</c> in Go: the encoder
    /// applies the rule to each element, so empty strings are left out.
    /// </summary>
    public void OptionalElements(string name, List<string> values)
    {
        foreach (string v in values)
        {
            Optional(name, v);
        }
    }

    /// <summary>An amount element: <c>&lt;X Ccy="BRL"&gt;10.50&lt;/X&gt;</c>.</summary>
    /// <exception cref="InvalidAmountException">The amount is negative or too large.</exception>
    public void Amount(string name, CurrencyAmount a)
    {
        string value = a.Value.ToIsoString();
        WriteIndent(1);
        _b.Append('<').Append(name).Append(" Ccy=\"");
        Escape(a.Ccy);
        _b.Append("\">");
        Escape(value);
        End(name);
    }

    public string Finish() => _b.ToString() + "\n";

    // Go's printer.writeIndent.
    private void WriteIndent(int depthDelta)
    {
        if (depthDelta < 0)
        {
            _depth--;
            if (_indentedIn)
            {
                _indentedIn = false;
                return;
            }

            _indentedIn = false;
        }

        if (_putNewline)
        {
            _b.Append('\n');
        }
        else
        {
            _putNewline = true;
        }

        _b.Append(' ', 2 * _depth);
        if (depthDelta > 0)
        {
            _depth++;
            _indentedIn = true;
        }
    }

    // Go's printer.EscapeString.
    private void Escape(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return;
        }

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '"': _b.Append("&#34;"); continue;
                case '\'': _b.Append("&#39;"); continue;
                case '&': _b.Append("&amp;"); continue;
                case '<': _b.Append("&lt;"); continue;
                case '>': _b.Append("&gt;"); continue;
                case '\t': _b.Append("&#x9;"); continue;
                case '\n': _b.Append("&#xA;"); continue;
                case '\r': _b.Append("&#xD;"); continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                _b.Append(c).Append(s[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c) || !Text.IsXmlChar(c))
            {
                _b.Append((char)0xFFFD);
            }
            else
            {
                _b.Append(c);
            }
        }
    }
}
