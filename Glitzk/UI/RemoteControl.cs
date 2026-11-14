using Glitzk.Services;
using Hexa.NET.ImGui;
using System.Numerics;

namespace Glitzk.UI;

class RemoteControl
{
    private readonly ImTextureRef texture;
    private readonly Sprite play;
    private readonly Sprite pause;
    private readonly Sprite skip;

    public RemoteControl(SpriteSheet sheet, Sprite play, Sprite pause, Sprite skip)
    {
        texture = sheet.Texture;
        this.play = play;
        this.pause = pause;
        this.skip = skip;
    }

    private static float ImageSide => ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.Y * 2;

    public static float Width
    {
        get
        {
            ImGuiStylePtr style = ImGui.GetStyle();
            float button = ImageSide + style.FramePadding.X * 2;
            return button * 2 + style.ItemSpacing.X;
        }
    }

    public void Draw(PlaybackController playback)
    {
        var imageSize = new Vector2(ImageSide);

        bool isRunning = playback.Mode == PlaybackMode.Running;
        Sprite toggle = isRunning ? pause : play;

        if (ImGui.ImageButton("##PlayPause", texture, imageSize, toggle.Uv0, toggle.Uv1))
        {
            if (isRunning)
                playback.Pause();
            else
                playback.Play();
        }

        ImGui.SameLine();

        if (ImGui.ImageButton("##Skip", texture, imageSize, skip.Uv0, skip.Uv1))
            playback.Skip();
    }
}
