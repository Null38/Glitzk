using Glitzk.Services;
using Hexa.NET.ImGui;
using System.Numerics;

namespace Glitzk.UI;

class SettingsPanel(ChatService chat, PlaybackSettings playbackSettings, LogWriter log)
{
    private enum SettingsCategory { Playback, Chzzk, Debug }

    private const int CredentialBufferSize = 128;
    private const float SidebarWidth = 140f;
    private const float SidebarMinWidth = 30f;
    private const float ContentMinWidth = 100f;
    private const float MinWindowWidth = 180f;

    private string clientId = chat.ClientId;
    private string clientSecret = chat.ClientSecret;
    private bool isOpen;
    private SettingsCategory selectedCategory;

    public string ClientId => clientId;
    public string ClientSecret => clientSecret;

    public void Open() => isOpen = true;

    public void Draw()
    {
        if (!isOpen)
            return;

        ImGui.SetNextWindowSizeConstraints(new Vector2(MinWindowWidth, 0), new Vector2(float.MaxValue, float.MaxValue));

        bool isVisible = ImGui.Begin("Settings", ref isOpen);

        if (isVisible)
        {
            DrawSidebar();
            ImGui.SameLine();
            DrawContent();
        }
        ImGui.End();
    }

    private void DrawSidebar()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.MenuBarBg));
        float sidebarMaxWidth = ImGui.GetContentRegionAvail().X - ContentMinWidth;
        ImGui.SetNextWindowSizeConstraints(new Vector2(SidebarMinWidth, 0), new Vector2(sidebarMaxWidth, float.MaxValue));
        ImGui.BeginChild("Sidebar", new Vector2(SidebarWidth, 0), ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.ResizeX);

        foreach (var category in Enum.GetValues<SettingsCategory>())
        {
            if (ImGui.Selectable(category.ToString(), selectedCategory == category))
                selectedCategory = category;
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void DrawContent()
    {
        ImGui.BeginChild("Content", Vector2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding);

        switch (selectedCategory)
        {
            case SettingsCategory.Playback:
                DrawPlayback();
                break;
            case SettingsCategory.Chzzk:
                DrawChzzk();
                break;
            case SettingsCategory.Debug:
                DrawDebug();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(selectedCategory), selectedCategory, null);
        }

        ImGui.EndChild();
    }

    private void DrawPlayback()
    {
        ImGui.Checkbox("자동 재생", ref playbackSettings.IsAutoPlayEnabled);
        ImGui.SetItemTooltip("치지직 채팅에 연결되면 자동으로 영상을 재생합니다.");

        ImGui.Checkbox("풀백 스킵", ref playbackSettings.IsFallbackInterruptEnabled);
        ImGui.SetItemTooltip("자동 재생목록의 영상이 재생 중일 때 신청이 들어오면,\n 해당 신청 영상을 즉시 재생합니다.");

        ImGui.Text("신청 영상 최대 길이 (분 단위, 0 = 제한 없음)");
        int maxMinutes = (int)playbackSettings.MaxRequestDuration.TotalMinutes;
        if (ImGui.InputInt("##Max Request Minutes", ref maxMinutes))
            playbackSettings.MaxRequestDuration = TimeSpan.FromMinutes(Math.Max(0, maxMinutes));
    }

    private void DrawChzzk()
    {
        ImGui.BeginDisabled(chat.Status != ChatStatus.Disconnected);

        // Todo: 여기에 치지직 개발자 센터 여는 OpenUrl()두기

        ImGui.Text("Chzzk Client Id");
        ImGui.InputText("##Client ID", ref clientId, CredentialBufferSize);

        ImGui.Text("Chzzk Client Secret");
        ImGui.InputText("##Client Secret", ref clientSecret, CredentialBufferSize, ImGuiInputTextFlags.Password);

        ImGui.EndDisabled();
    }

    private void DrawDebug()
    {
        // Todo: 로컬 입력테스트
        // Todo: 플레이어 초기화
        ImGui.Checkbox("Skip search results without length", ref playbackSettings.IsSearchLengthFilterEnabled);
        ImGui.Checkbox("로그 저장", ref log.IsEnabled);
    }
}
