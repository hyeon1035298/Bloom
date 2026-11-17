using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Bloom.App.Mcp;

/// `Bloom.exe --mcp <pipe> [<conversation>]`: what Claude Code or Codex starts as the "bloom" MCP server. It only
/// carries the stdio lines to the running Bloom over a named pipe that only this Windows user can open (McpServer
/// answers them) and back — no window, no WPF, no data folder. It ends when either side closes. Its first line is the
/// hello: the run's key from its environment (McpKey), and a conversation id for Codex and omp runs, so Bloom knows whose
/// chat to ask before an action.
internal static class McpBridge
{
    public static int Run(string pipe, string? conversation)
    {
        if (Core.McpKey.Take() is not { } key)
        {
            Console.Error.WriteLine("Bloom's key is missing: start this through a Bloom chat.");
            return 1;
        }
        try
        {
            using var link = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            link.Connect(3000);
            string hello = conversation is null
                ? JsonSerializer.Serialize(new { bloom_key = key })
                : JsonSerializer.Serialize(new { bloom_key = key, bloom_conversation = conversation });
            byte[] bytes = Encoding.UTF8.GetBytes(hello + "\n");
            link.Write(bytes, 0, bytes.Length);
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();
            var up = input.CopyToAsync(link);
            var down = link.CopyToAsync(output);
            Task.WaitAny(up, down);
            return 0;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // Bloom is not running (or is another user's): the CLI sees the server fail and goes on without it
            Console.Error.WriteLine("Bloom is not running: " + e.Message);
            return 1;
        }
    }
}
