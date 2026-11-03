using Glitzk.Services;
using Glitzk.Services.Commands;
using Hexa.NET.ImGui;
using SDL3;
using System;
using System.Numerics;
using System.Reflection.Emit;

namespace Glitzk.UI;

class SettingsPanel(ChatService chat, PlaybackSettings playbackSettings, LogWriter log,
    IReadOnlyList<ChatCommand> commands, Dictionary<string, ChatCommand> commandsByTrigger)
{
    private enum SettingsCategory { Playback, Chzzk, Commands, Debug }

    private const int CredentialBufferSize = 128;

    private const float SidebarWidth = 140f;
    private const float SidebarMinWidth = 30f;
    private const float ContentMinWidth = 100f;
    private const float MinWindowWidth = 180f;
    private static readonly Vector2 DefaultWindowSize = new(420f, 500f);

    private bool isOpen;
    private SettingsCategory selectedCategory;

    public void Open() => isOpen = true;

    public void Draw()
    {
        if (!isOpen)
            return;

        ImGui.SetNextWindowSize(DefaultWindowSize, ImGuiCond.FirstUseEver);
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
        ImGui.PushItemWidth(-float.Epsilon);

        switch (selectedCategory)
        {
            case SettingsCategory.Playback:
                DrawPlayback();
                break;
            case SettingsCategory.Chzzk:
                DrawChzzk();
                break;
            case SettingsCategory.Commands:
                DrawCommands();
                break;
            case SettingsCategory.Debug:
                DrawDebug();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(selectedCategory), selectedCategory, null);
        }

        ImGui.PopItemWidth();
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

    private string clientId = chat.ClientId;
    private string clientSecret = chat.ClientSecret;
    public string ClientId => clientId;
    public string ClientSecret => clientSecret;

    private const string ChzzkDeveloperCenterUrl = "https://developers.chzzk.naver.com/";

    private void DrawChzzk()
    {
        ImGui.BeginDisabled(chat.Status != ChatStatus.Disconnected);

        if (ImGui.Button("치지직 개발자 센터 열기"))
        {
            SDL.OpenURL(ChzzkDeveloperCenterUrl);
        }

        ImGui.Text("Chzzk Client Id");
        ImGui.InputText("##Client ID", ref clientId, CredentialBufferSize);

        ImGui.Text("Chzzk Client Secret");
        ImGui.InputText("##Client Secret", ref clientSecret, CredentialBufferSize, ImGuiInputTextFlags.Password);

        ImGui.EndDisabled();
    }

    private const int TriggerBufferSize = 32;
    private const float CommandComboWidth = 100f;
    private const float TriggerListHeight = 150f;
    private const string AddButtonLabel = "추가";

    private string newTrigger = string.Empty;
    private int newTriggerCommandIndex;
    private int selectedCommandIndex;

    private void DrawCommands()
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        float addButtonWidth = ImGui.CalcTextSize(AddButtonLabel).X + style.FramePadding.X * 2;
        ImGui.SetNextItemWidth(-(CommandComboWidth + addButtonWidth + style.ItemSpacing.X * 2));
        ImGui.InputText("##New Trigger", ref newTrigger, TriggerBufferSize);
        ImGui.SameLine();

        ImGui.SetNextItemWidth(CommandComboWidth);
        DrawCommandCombo("##New Trigger Command", ref newTriggerCommandIndex);
        ImGui.SameLine();

        string trigger = newTrigger.Trim();
        bool canAdd = trigger.Length > 0 && !trigger.Contains(' ') && !commandsByTrigger.ContainsKey(trigger);

        ImGui.BeginDisabled(!canAdd);
        if (ImGui.Button(AddButtonLabel))
        {
            commandsByTrigger[trigger] = commands[newTriggerCommandIndex];
            newTrigger = string.Empty;
        }
        ImGui.EndDisabled();

        if (ImGui.BeginTable("TriggerTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable, new Vector2(0, TriggerListHeight)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("명령어", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("실행", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize);
            ImGui.TableSetupColumn("##Delete", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
            ImGui.TableHeadersRow();

            string? removedTrigger = null;
            foreach (var (existing, command) in commandsByTrigger)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGuiEx.TextEllipsisWithTooltip(existing, ImGui.GetContentRegionAvail().X);
                ImGui.TableNextColumn();
                ImGuiEx.TextEllipsisWithTooltip(command.Name, ImGui.GetContentRegionAvail().X);
                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"삭제##{existing}"))
                    removedTrigger = existing;
            }

            if (removedTrigger != null)
                commandsByTrigger.Remove(removedTrigger);

            ImGui.EndTable();
        }

        ImGui.NewLine();

        DrawCommandCombo("##Command", ref selectedCommandIndex);
        commands[selectedCommandIndex].DrawSettings();
    }

    private void DrawCommandCombo(string label, ref int index)
    {
        if (!ImGui.BeginCombo(label, commands[index].Name))
            return;

        for (int i = 0; i < commands.Count; i++)
        {
            if (ImGui.Selectable(commands[i].Name, i == index))
                index = i;
        }
        ImGui.EndCombo();
    }

    private void DrawDebug()
    {
        // Todo: 로컬 입력테스트
        // Todo: 플레이어 초기화
        ImGui.Checkbox("로그 저장", ref log.IsEnabled);
        ImGui.Checkbox("Skip search results without length", ref playbackSettings.IsSearchLengthFilterEnabled);
    }
}
