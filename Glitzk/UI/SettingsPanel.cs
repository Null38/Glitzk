using Glitzk.Services;
using Hexa.NET.ImGui;

namespace Glitzk.UI;

class SettingsPanel(ChatService chat)
{
    private const int CredentialBufferSize = 128;

    private string clientId = chat.ClientId;
    private string clientSecret = chat.ClientSecret;
    private bool isOpen;

    public string ClientId => clientId;
    public string ClientSecret => clientSecret;

    public void Open() => isOpen = true;

    public void Draw()
    {
        if (!isOpen)
            return;

        if (ImGui.Begin("Settings", ref isOpen))
        {
            ImGui.BeginDisabled(chat.Status != ChatStatus.Disconnected);

            // Todo: 여기에 치지직 개발자 센터 여는 OpenUrl()두기

            ImGui.Text("Chzzk Client Id");
            ImGui.InputText("##Client ID", ref clientId, CredentialBufferSize);

            ImGui.Text("Chzzk Client Secret");
            ImGui.InputText("##Client Secret", ref clientSecret, CredentialBufferSize, ImGuiInputTextFlags.Password);

            ImGui.EndDisabled();
        }
        ImGui.End();
    }
}
