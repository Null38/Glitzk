using Hexa.NET.ImGui;

namespace Glitzk.UI;

class FallbackInput(Action<string> onAdd)
{
    private const int InputBufferSize = 256;

    private string input = string.Empty;

    public void Draw(float width)
    {
        float btnWidth = ImGui.CalcTextSize("Add").X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SetNextItemWidth(width - btnWidth - ImGui.GetStyle().ItemSpacing.X);
        bool isSubmitted = ImGui.InputText("##FallbackInput", ref input, InputBufferSize, ImGuiInputTextFlags.EnterReturnsTrue);

        ImGui.SameLine();
        isSubmitted |= ImGui.Button("Add##Fallback");

        if (isSubmitted && !string.IsNullOrWhiteSpace(input))
        {
            onAdd(input);
            input = string.Empty;
        }
    }
}
