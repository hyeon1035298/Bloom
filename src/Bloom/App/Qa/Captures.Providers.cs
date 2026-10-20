using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Bloom.Core;
using Bloom.Ui;

namespace Bloom.App;

/// `--qa-set providers`: a fake provider (an echo script in a temp folder) answers "ec …" in the search line; "g …" is a
/// quicklink that the provider of the same keyword overrides; nothing of the user's is read or changed.
internal static partial class Captures
{
    private static async Task ProviderScreens(Host host, Launcher w, string dir)
    {
        string tmp = Path.Combine(dir, "provider-tmp");
        Directory.CreateDirectory(tmp);
        string script = Path.Combine(tmp, "echo.ps1");
        File.WriteAllText(script, """
            param([string]$Words)
            [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
            @{ items = @(
                @{ title = "echo: $Words"; subtitle = "url · https://example.com/?q=$Words"; action = @{ type = "url"; value = "https://example.com/?q=$Words" } },
                @{ title = "복사: $Words"; subtitle = "copy"; action = @{ type = "copy"; value = $Words } },
                @{ title = "실행 예: $Words"; subtitle = "run"; action = @{ type = "run"; value = @("cmd", "/c", "exit") } },
                @{ title = "거부될 주소"; subtitle = "file: scheme"; action = @{ type = "url"; value = "file:///C:/Windows/notepad.exe" } }
            ) } | ConvertTo-Json -Depth 6 -Compress
            """);
        string[] command = { "powershell", "-NoProfile", "-File", script };
        var config = new[]
        {
            new { keyword = "ec", name = "에코 검색", command, cwd = tmp, minChars = 1, timeoutMs = 8000 },
            new { keyword = "g", name = "에코 구글", command, cwd = tmp, minChars = 1, timeoutMs = 8000 },
        };
        File.WriteAllText(Paths.In("providers.json"), JsonSerializer.Serialize(config));
        foreach (string theme in new[] { "dark", "light" })
        {
            Theme.Apply(theme);
            await Shot(w, dir, theme + "-pv1-keyword", Mode.Root, "ec", 700);
            await Shot(w, dir, theme + "-pv2-hint", Mode.Root, "ec ", 700);
            await Shot(w, dir, theme + "-pv3-results", Mode.Root, "ec 안녕 world", 6000);
            await Shot(w, dir, theme + "-pv4-overrides-quicklink", Mode.Root, "g 고양이", 6000);
        }
        Theme.Apply("dark");
        File.Delete(Paths.In("providers.json"));
        Directory.Delete(tmp, true);
    }
}
