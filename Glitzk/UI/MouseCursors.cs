using Hexa.NET.ImGui;
using SDL3;

namespace Glitzk.UI;

enum CustomCursor
{
    Grab = 0,
    Grabbing = 1,
    Count = 2
}

class MouseCursors : IDisposable
{
    private const int SpriteSize = 32;

    private CustomCursor? requested;
    private nint applied;

    private readonly nint[] system = new nint[(int)ImGuiMouseCursor.Count];

    private readonly nint[] custom = new nint[(int)CustomCursor.Count];

    private void InitializeCustomCursors(SpriteSheet sheet)
    {
        custom[(int)CustomCursor.Grab] = sheet.CreateCursor(0, 0, SpriteSize, SpriteSize, 16, 16);
        custom[(int)CustomCursor.Grabbing] = sheet.CreateCursor(SpriteSize, 0, SpriteSize, SpriteSize, 16, 16);
    }

    public MouseCursors(SpriteSheet sheet)
    {
        for (var cursor = ImGuiMouseCursor.Arrow; cursor < ImGuiMouseCursor.Count; cursor++)
        {
            SDL.SystemCursor id = cursor switch
            {
                ImGuiMouseCursor.Arrow => SDL.SystemCursor.Default,
                ImGuiMouseCursor.TextInput => SDL.SystemCursor.Text,
                ImGuiMouseCursor.ResizeAll => SDL.SystemCursor.Move,
                ImGuiMouseCursor.ResizeNs => SDL.SystemCursor.NSResize,
                ImGuiMouseCursor.ResizeEw => SDL.SystemCursor.EWResize,
                ImGuiMouseCursor.ResizeNesw => SDL.SystemCursor.NESWResize,
                ImGuiMouseCursor.ResizeNwse => SDL.SystemCursor.NWSEResize,
                ImGuiMouseCursor.Hand => SDL.SystemCursor.Pointer,
                ImGuiMouseCursor.Wait => SDL.SystemCursor.Wait,
                ImGuiMouseCursor.Progress => SDL.SystemCursor.Progress,
                ImGuiMouseCursor.NotAllowed => SDL.SystemCursor.NotAllowed,
                _ => throw new NotImplementedException()
            };

            system[(int)cursor] = SDL.CreateSystemCursor(id);
            if (system[(int)cursor] == nint.Zero)
                throw new InvalidOperationException($"Error creating system cursor {id}: {SDL.GetError()}");
        }

        InitializeCustomCursors(sheet);

        applied = system[(int)ImGuiMouseCursor.Arrow];
    }

    public void Set(CustomCursor cursor)
    {
        requested = cursor;
    }

    public void Apply()
    {
        ImGuiMouseCursor imguiCursor = ImGui.GetMouseCursor();

        nint target;
        if (requested is CustomCursor request)
            target = custom[(int)request];
        else if (imguiCursor == ImGuiMouseCursor.None)
            target = nint.Zero;
        else
            target = system[(int)imguiCursor];

        requested = null;

        if (target == applied)
            return;
        applied = target;

        if (target == nint.Zero)
        {
            SDL.HideCursor();
            return;
        }

        SDL.SetCursor(target);
        SDL.ShowCursor();
    }
    
    public void Dispose()
    {
        foreach (nint cursor in system)
            SDL.DestroyCursor(cursor);

        foreach (nint cursor in custom)
            SDL.DestroyCursor(cursor);
    }
}
