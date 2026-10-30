using Hexa.NET.ImGui;
using System.Numerics;

namespace Glitzk.UI;

class AlertPopup
{
    private readonly Queue<string> messages = new();

    public void Show(string message) => messages.Enqueue(message);

    public void Draw()
    {
        if (messages.Count == 0)
            return;

        if (!ImGui.IsPopupOpen("Alert"))
            ImGui.OpenPopup("Alert");

        ImGui.SetNextWindowSize(new Vector2(400, 0));
        if (ImGui.BeginPopupModal("Alert"))
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextUnformatted(messages.Peek());
            ImGui.PopTextWrapPos();

            if (ImGui.Button("OK"))
            {
                messages.Dequeue();
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}
