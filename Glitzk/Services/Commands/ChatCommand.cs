using Hexa.NET.ImGui;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Glitzk.Services.Commands;

abstract partial class ChatCommand
{
    private const int ReplyBufferSize = 256;
    private const int MaxReplyLength = 90;

    private readonly Action<string> send;

    public abstract string Name { get; }

    protected ChatCommand(Action<string> send)
    {
        this.send = send;
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    public abstract void Handle(ChatMessage message, string args);

    public abstract void FromXml(XElement element);

    public abstract XElement ToXml();

    public abstract void DrawSettings();

    protected void Reply(string template, Dictionary<string, string> values)
    {
        string text = Placeholder().Replace(template, match => values.GetValueOrDefault(match.Groups[1].Value, match.Value));

        if (text.Length > MaxReplyLength)
        {
            int cut = char.IsHighSurrogate(text[MaxReplyLength - 1]) ? MaxReplyLength - 1 : MaxReplyLength;
            text = text[..cut];
        }

        send(text);
    }

    protected static string ReadText(XElement element, string name, string fallback) =>
        (string?)element.Element(name) ?? fallback;

    protected static void DrawReply(string label, ref string template)
    {
        ImGui.Text(label);
        ImGui.InputText($"##{label}", ref template, ReplyBufferSize);
    }
}
