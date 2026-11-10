using ChzzkApi.Models;
using Hexa.NET.ImGui;
using System.Xml.Linq;

namespace Glitzk.Services.Commands;

[Flags]
enum UserRoleFlags
{
    None = 0,
    Streamer = 1 << 0,
    CommonUser = 1 << 1,
    ChannelManager = 1 << 2,
    ChatManager = 1 << 3,
    Requester = 1 << 4
}

static class UserRoleCodeExtensions
{
    public static UserRoleFlags ToFlag(this UserRoleCode role) => role switch
    {
        UserRoleCode.Streamer => UserRoleFlags.Streamer,
        UserRoleCode.CommonUser => UserRoleFlags.CommonUser,
        UserRoleCode.StreamingChannelManager => UserRoleFlags.ChannelManager,
        UserRoleCode.StreamingChatManager => UserRoleFlags.ChatManager,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    public static string ToRoleString(this UserRoleFlags flag) => flag switch
    {
        UserRoleFlags.Streamer => "스트리머",
        UserRoleFlags.ChannelManager => "채널 관리자",
        UserRoleFlags.ChatManager => "채팅 관리자",
        UserRoleFlags.CommonUser => "일반 시청자",
        UserRoleFlags.Requester => "신청자",
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, null)
    };
}

class SkipCommand(PlaybackController playback, Action<string> send)
    : ChatCommand(send)
{
    private static readonly TextTemplate.TemplateArg[] NicknameArgs = [new("nickname", 30)];
    private static readonly TextTemplate.TemplateArg[] TitleArgs = [new("nickname", 30), new("title", 40)];

    private static readonly UserRoleFlags[] SelectableRoles =
    [
        UserRoleFlags.Streamer,
        UserRoleFlags.ChannelManager,
        UserRoleFlags.ChatManager,
        UserRoleFlags.CommonUser,
        UserRoleFlags.Requester
    ];

    private readonly TextTemplate skipped = new("스킵했습니다", TitleArgs);
    private readonly TextTemplate denied = new("스킵 권한이 없습니다.", TitleArgs);
    private readonly TextTemplate nothingPlaying = new("재생 중인 곡이 없습니다.", NicknameArgs);

    private UserRoleFlags allowedRoles = UserRoleFlags.Streamer | UserRoleFlags.ChannelManager | UserRoleFlags.ChatManager | UserRoleFlags.Requester;

    public override string Name => "Skip";

    public override void Handle(ChatMessage message, string args)
    {
        string nickname = message.Info.Nickname;

        VideoRequest? current = playback.Current;
        if (playback.Mode == PlaybackMode.Idle || current == null)
        {
            Reply(nothingPlaying, nickname);
            return;
        }

        string title = current.Video.Title;

        UserRoleFlags senderFlags = message.Role.ToFlag();
        if (current.Info?.SenderId == message.Info.SenderId)
            senderFlags |= UserRoleFlags.Requester;

        if ((allowedRoles & senderFlags) == 0)
        {
            Reply(denied, nickname, title);
            return;
        }

        playback.Skip();
        Reply(skipped, nickname, title);
    }

    public override void FromXml(XElement element)
    {
        skipped.Template = ReadText(element, "Skipped", skipped.Template);
        denied.Template = ReadText(element, "Denied", denied.Template);
        nothingPlaying.Template = ReadText(element, "NothingPlaying", nothingPlaying.Template);

        if (int.TryParse((string?)element.Element("AllowedRoles"), out int roles))
            allowedRoles = (UserRoleFlags)roles;
    }

    public override XElement ToXml() => new("Command",
        new XAttribute("name", Name),
        new XElement("AllowedRoles", (int)allowedRoles),
        new XElement("Skipped", skipped.Template),
        new XElement("Denied", denied.Template),
        new XElement("NothingPlaying", nothingPlaying.Template));

    public override void DrawSettings()
    {
        if (ImGui.BeginCombo("##AllowedRoles", "스킵 허용 역할"))
        {
            foreach (UserRoleFlags role in SelectableRoles)
            {
                bool isAllowed = allowedRoles.HasFlag(role);
                if (ImGui.Checkbox(role.ToRoleString(), ref isAllowed))
                    allowedRoles ^= role;
            }

            ImGui.EndCombo();
        }

        DrawReply("스킵 완료 시", skipped);
        DrawReply("권한이 없을 시", denied);
        DrawReply("재생 중이 아닐 시", nothingPlaying);
    }
}
