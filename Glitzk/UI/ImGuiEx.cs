using Hexa.NET.ImGui;

namespace Glitzk.UI;

static class ImGuiEx
{
    const string ellipsis = "...";

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
}
