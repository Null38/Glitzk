using Glitzk.Services;
using Hexa.NET.ImGui;
using System.Numerics;

namespace Glitzk.UI;

static class ImGuiEx
{
    const string ellipsis = "...";

    const float SquiggleHalfWave = 2f;
    const float SquiggleHeight = 3f;

    static readonly Vector4 TemplateErrorColor = new(1f, 0.3f, 0.3f, 1f);

    public static void TextEllipsisWithTooltip(string text, float size)
    {
        string displayText = text;

        if (ImGui.CalcTextSize(text).X > size)
        {
            float ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;

            int left = 0;
            int right = text.Length;
            int best = 0;

            while (left <= right)
            {
                int mid = (left + right) / 2;

                string candidate = text[..mid];
                float width = ImGui.CalcTextSize(candidate).X;

                if (width + ellipsisWidth <= size)
                {
                    best = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }
            displayText = text[..best] + ellipsis;
        }

        ImGui.TextUnformatted(displayText);

        if (ImGui.IsItemHovered() && displayText != text)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(text);
            ImGui.EndTooltip();
        }
    }

    public static bool InputTemplate(string label, TextTemplate template, nuint bufferSize)
    {
        string format = template.Template;
        bool isChanged = ImGui.InputText(label, ref format, bufferSize);
        if (isChanged)
            template.Template = format;

        uint id = ImGui.GetItemID();
        
        float scrollX = ImGuiP.GetActiveID() == id ? ImGuiP.GetInputTextState(id).Scroll.X : 0f;
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        Vector2 origin = min + ImGui.GetStyle().FramePadding - new Vector2(scrollX, 0f);

        ImGui.PushClipRect(min, max, true);
        DrawTemplateErrors(template, format, origin);
        ImGui.PopClipRect();
        return isChanged;
    }

    static void DrawTemplateErrors(TextTemplate template, string format, Vector2 origin)
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint color = ImGui.GetColorU32(TemplateErrorColor);
        float underlineY = origin.Y + ImGui.GetTextLineHeight();

        foreach (TextTemplate.TemplateError error in template.Errors)
        {
            float x0 = origin.X + ImGui.CalcTextSize(format[..error.Start]).X;
            float x1 = origin.X + ImGui.CalcTextSize(format[..(error.Start + error.Length)]).X;
            DrawSquiggle(drawList, x0, x1, underlineY, color);

            if (ImGui.IsMouseHoveringRect(new Vector2(x0, origin.Y), new Vector2(x1, underlineY + SquiggleHeight)))
                ImGui.SetTooltip(error.Message);
        }
    }

    static void DrawSquiggle(ImDrawListPtr drawList, float x0, float x1, float y, uint color)
    {
        bool isRising = false;
        for (float x = x0; x < x1; x += SquiggleHalfWave)
        {
            float next = MathF.Min(x + SquiggleHalfWave, x1);
            float fromY = isRising ? y + SquiggleHeight : y;
            float toY = isRising ? y : y + SquiggleHeight;
            drawList.AddLine(new Vector2(x, fromY), new Vector2(next, toY), color);
            isRising = !isRising;
        }
    }
}
