using Glitzk.Services;
using Hexa.NET.ImGui;
using System.Numerics;

namespace Glitzk.UI;

class QueuePanel
{
    private const float ListMaxWidth = 500f;
    private const int ListHeight = 300;
    private const int VideoItemHeight = 60;
    private const float HandleSize = 16f;

    public unsafe void Draw(LinkedList<VideoInfo> requests)
    {
        float width = MathF.Min(ListMaxWidth, ImGui.GetContentRegionAvail().X);
        ImGui.BeginChild("QueueList", new Vector2(width, ListHeight), ImGuiChildFlags.Borders);

        int i = 0;
        for (var item = requests.First; item != null; item = item.Next)
        {
            ImGui.PushID(i++);
            ImGui.BeginChild("item", new Vector2(0, VideoItemHeight), ImGuiChildFlags.Borders);

            ImGui.Button("##Handle", new Vector2(HandleSize, ImGui.GetContentRegionAvail().Y));

            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);//임시 커서

            if (ImGui.BeginDragDropSource())
            {
                ImGui.SetDragDropPayload("QueueItem", null, 0);
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);//임시 커서
                ImGui.TextUnformatted(item.Value.Title);
                ImGui.EndDragDropSource();
            }

            ImGui.SameLine();
            ImGui.BeginGroup();

            float btnWidth = ImGui.CalcTextSize("Remove").X + ImGui.GetStyle().FramePadding.X * 2;
            float spacing = ImGui.GetStyle().ItemSpacing.X;

            ImGuiEx.TextEllipsisWithTooltip(item.Value.Title,ImGui.GetContentRegionAvail().X - btnWidth - spacing);
            ImGui.TextUnformatted(item.Value.DurationString());
            ImGui.EndGroup();

            ImGui.SameLine();
            float btnHeight = ImGui.GetContentRegionAvail().Y;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - btnWidth - ImGui.GetStyle().WindowPadding.X);

            if (ImGui.Button("Remove", new Vector2(btnWidth, btnHeight)))
            {
                requests.Remove(item);
            }

            ImGui.EndChild();
            ImGui.PopID();
        }

        ImGui.EndChild();
    }
}
