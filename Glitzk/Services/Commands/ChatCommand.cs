using Glitzk.UI;
using Hexa.NET.ImGui;
using System.Numerics;
using System.Xml.Linq;

namespace Glitzk.Services.Commands;

abstract class ChatCommand
{
    private const int ReplyBufferSize = 256;
    private const int MaxReplyLength = 90;
    private static readonly Vector4 LengthExceededColor = new(1f, 0.3f, 0.3f, 1f);

    private readonly Action<string> send;

    public abstract string Name { get; }

    protected ChatCommand(Action<string> send)
    {
        this.send = send;
    }

    public abstract void Handle(ChatMessage message, string args);

    public abstract void FromXml(XElement element);

    public abstract XElement ToXml();

    public abstract void DrawSettings();

    protected void Reply(TextTemplate template, params ReadOnlySpan<string> values)
    {
        if (!template.IsValid || template.MaxLength > MaxReplyLength)
            return;

        send(template.Format(values));
    }

    protected static string ReadText(XElement element, string name, string fallback) =>
        (string?)element.Element(name) ?? fallback;

    protected static void DrawReply(string label, TextTemplate template)
    {
        ImGui.Text(label);

        string lengthText = $"{(template.IsValid ? template.MaxLength.ToString("00") : "-")} / {MaxReplyLength}";
        ImGui.SetNextItemWidth(-(ImGui.CalcTextSize(lengthText).X + ImGui.GetStyle().ItemSpacing.X));
        ImGuiEx.InputTemplate($"##{label}", template, ReplyBufferSize);

        ImGui.SameLine();
        if (template.MaxLength > MaxReplyLength)
        {
            ImGui.TextColored(LengthExceededColor, lengthText);
        }
        else
        {
            ImGui.TextUnformatted(lengthText);
        }
    }
}
