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
    private const float DropLineThickness = 2f;
    private const float RequesterFontScale = 0.85f;

    private static readonly Vector4 RowBackground = new(App.BackgroundBrightness, App.BackgroundBrightness, App.BackgroundBrightness, 1f);

    private static LinkedListNode<VideoRequest>? dragged;

    public static unsafe void DrawRequests(
        LinkedList<VideoRequest> requests,
        Action<LinkedListNode<VideoRequest>> remove,
        Action<LinkedListNode<VideoRequest>, LinkedListNode<VideoRequest>?> move,
        float width)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.MenuBarBg));
        ImGui.BeginChild("QueueList", new Vector2(width, ListHeight), ImGuiChildFlags.Borders);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, RowBackground);

        float requesterHeight = ImGui.GetTextLineHeightWithSpacing();
        float disabledAlpha = ImGui.GetStyle().DisabledAlpha;
        bool isDragging = !ImGui.GetDragDropPayload().IsNull;

        LinkedListNode<VideoRequest>? removed = null;
        LinkedListNode<VideoRequest>? dropped = null;
        LinkedListNode<VideoRequest>? dropBefore = null;

        int i = 0;
        for (var item = requests.First; item != null; item = item.Next)
        {
            bool isDraggedRow = isDragging && item == dragged;

            ImGui.PushID(i++);

            if (isDraggedRow)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, disabledAlpha);

            ImGui.BeginChild("item", new Vector2(0, VideoItemHeight + requesterHeight), ImGuiChildFlags.Borders);

            ImGui.Button("##Handle", new Vector2(HandleSize, ImGui.GetContentRegionAvail().Y));

            if (isDraggedRow)
                ImGui.PopStyleVar();

            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);//임시 커서

            if (ImGui.BeginDragDropSource())
            {
                dragged = item;
                ImGui.SetDragDropPayload("QueueItem", null, 0);
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);//임시 커서
                ImGui.TextUnformatted(item.Value.Video.Title);
                ImGui.EndDragDropSource();
            }

            if (isDraggedRow)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, disabledAlpha);

            ImGui.SameLine();

            ImGui.BeginGroup();

            if (DrawVideoRow(item.Value.Video, ImGui.GetContentRegionAvail().Y - requesterHeight))
                removed = item;

            ImGui.PushFont(ImGui.GetFont(), ImGui.GetFontSize() * RequesterFontScale);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
            ImGui.TextUnformatted($"신청자: {item.Value.Info?.Nickname ?? "디버그"}");
            ImGui.PopStyleColor();
            ImGui.PopFont();

            ImGui.EndGroup();

            ImGui.EndChild();

            if (isDraggedRow)
                ImGui.PopStyleVar();

            if (ImGui.BeginDragDropTarget())
            {
                Vector2 min = ImGui.GetItemRectMin();
                Vector2 max = ImGui.GetItemRectMax();
                bool isUpperHalf = ImGui.GetMousePos().Y < (min.Y + max.Y) / 2;
                var before = isUpperHalf ? item : item.Next;

                if (before != dragged && before != dragged?.Next)
                {
                    float gapOffset = ImGui.GetStyle().ItemSpacing.Y / 2;
                    float lineY = isUpperHalf ? min.Y - gapOffset : max.Y + gapOffset;

                    ImGui.GetWindowDrawList().AddLine(
                        new Vector2(min.X, lineY),
                        new Vector2(max.X, lineY),
                        ImGui.GetColorU32(ImGuiCol.DragDropTarget),
                        DropLineThickness);

                    if (!ImGui.AcceptDragDropPayload("QueueItem", ImGuiDragDropFlags.AcceptNoDrawDefaultRect).IsNull)
                    {
                        dropped = dragged;
                        dropBefore = before;
                    }
                }

                ImGui.EndDragDropTarget();
            }

            ImGui.PopID();
        }

        ImGui.PopStyleColor();
        ImGui.EndChild();
        ImGui.PopStyleColor();

        if (removed != null)
            remove(removed);

        if (dropped?.List != null)
            move(dropped, dropBefore);
    }

    public static void DrawFallbacks(IReadOnlyList<VideoInfo> fallbacks, Action<VideoInfo> remove, float width)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.MenuBarBg));
        ImGui.BeginChild("FallbackList", new Vector2(width, ListHeight), ImGuiChildFlags.Borders);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, RowBackground);

        VideoInfo? removed = null;

        for (int i = 0; i < fallbacks.Count; i++)
        {
            ImGui.PushID(i);
            ImGui.BeginChild("item", new Vector2(0, VideoItemHeight), ImGuiChildFlags.Borders);

            if (DrawVideoRow(fallbacks[i], ImGui.GetContentRegionAvail().Y))
                removed = fallbacks[i];

            ImGui.EndChild();
            ImGui.PopID();
        }

        ImGui.PopStyleColor();
        ImGui.EndChild();
        ImGui.PopStyleColor();

        if (removed != null)
            remove(removed);
    }

    private static bool DrawVideoRow(VideoInfo info, float height)
    {
        ImGui.BeginGroup();

        float btnWidth = ImGui.CalcTextSize("Remove").X + ImGui.GetStyle().FramePadding.X * 2;
        float spacing = ImGui.GetStyle().ItemSpacing.X;

        ImGuiEx.TextEllipsisWithTooltip(info.Title, ImGui.GetContentRegionAvail().X - btnWidth - spacing);
        ImGui.TextUnformatted(info.DurationString());
        ImGui.EndGroup();

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - btnWidth - ImGui.GetStyle().WindowPadding.X);

        return ImGui.Button("Remove", new Vector2(btnWidth, height));
    }
}
