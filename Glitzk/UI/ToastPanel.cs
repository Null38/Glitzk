using Hexa.NET.ImGui;
using SDL3;
using System.Numerics;

namespace Glitzk.UI;

class ToastPanel
{
    private const ulong DurationMs = 10000;
    private const ulong FadeOutMs = 1000;
    private const int MaxCount = 10;
    private const float MaxTextWidth = 320f;

    private readonly List<(int Slot, string Message, ulong ExpiresAtMs)> toasts = [];
    private int nextSlot;

    public void Show(string message)
    {
        if (toasts.Count == MaxCount)
            toasts.RemoveAt(0);

        toasts.Add((nextSlot, message, SDL.GetTicks() + DurationMs + FadeOutMs));

        nextSlot = (nextSlot + 1) % MaxCount;
    }

    public void Draw()
    {
        ulong now = SDL.GetTicks();
        toasts.RemoveAll(toast => toast.ExpiresAtMs <= now);

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        Vector2 margin = ImGui.GetStyle().ItemSpacing;
        Vector2 position = viewport.WorkPos + viewport.WorkSize - margin;

        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            var (slot, message, expiresAtMs) = toasts[i];

            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, MathF.Min(1f, (float)(expiresAtMs - now) / FadeOutMs));
            ImGui.SetNextWindowViewport(viewport.ID);
            ImGui.SetNextWindowPos(position, ImGuiCond.Always, new Vector2(1, 1));
            ImGui.Begin($"##Toast{slot}",
                  ImGuiWindowFlags.NoDecoration
                | ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoNav
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoSavedSettings);

            ImGuiEx.TextEllipsisWithTooltip(message, MaxTextWidth);

            position.Y -= ImGui.GetWindowHeight() + margin.Y;

            ImGui.End();
            ImGui.PopStyleVar();
        }
    }
}
