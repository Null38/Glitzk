using Glitzk.Services;
using Hexa.NET.ImGui;
using System;
using System.Numerics;

namespace Glitzk.UI;

static class VideoListPanel
{
    private const int ListHeight = 300;
    private const int VideoItemHeight = 60;
    private const float HandleSize = 16f;

    public static unsafe void DrawRequests(LinkedList<VideoInfo> requests, float width)
    {
        ImGui.BeginChild("QueueList", new Vector2(width, ListHeight), ImGuiChildFlags.Borders);

        LinkedListNode<VideoInfo>? removed = null;

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

            if (DrawVideoRow(item.Value))
                removed = item;

            ImGui.EndChild();
            ImGui.PopID();
        }

        ImGui.EndChild();

        if (removed != null)
            requests.Remove(removed);
    }

    public static void DrawFallbacks(IReadOnlyList<VideoInfo> fallbacks, Action<VideoInfo> remove, float width)
    {
        ImGui.BeginChild("FallbackList", new Vector2(width, ListHeight), ImGuiChildFlags.Borders);

        VideoInfo? removed = null;

        for (int i = 0; i < fallbacks.Count; i++)
        {
            ImGui.PushID(i);
            ImGui.BeginChild("item", new Vector2(0, VideoItemHeight), ImGuiChildFlags.Borders);

            if (DrawVideoRow(fallbacks[i]))
                removed = fallbacks[i];

            ImGui.EndChild();
            ImGui.PopID();
        }

        ImGui.EndChild();

        if (removed != null)
            remove(removed);
    }

    private static bool DrawVideoRow(VideoInfo info)
    {
        ImGui.BeginGroup();

        float btnWidth = ImGui.CalcTextSize("Remove").X + ImGui.GetStyle().FramePadding.X * 2;
        float spacing = ImGui.GetStyle().ItemSpacing.X;

        ImGuiEx.TextEllipsisWithTooltip(info.Title, ImGui.GetContentRegionAvail().X - btnWidth - spacing);
        ImGui.TextUnformatted(info.DurationString());
        ImGui.EndGroup();

        ImGui.SameLine();
        float btnHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - btnWidth - ImGui.GetStyle().WindowPadding.X);

        return ImGui.Button("Remove", new Vector2(btnWidth, btnHeight));
    }
}
