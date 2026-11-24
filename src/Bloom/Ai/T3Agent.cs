using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bloom.App;
using Bloom.Core;

namespace Bloom.Ai;

/// Bloom as an agent of T3 Code, for the add-ons' 보내기 and 정지 (the Run Card's cards, PillTaskbar's chat panel)
/// and the AI chat's T3 side (T3Chats): words typed for a T3 session go to its thread through T3's own
/// agent tools (MCP at `<origin>/mcp`) — the answer to a question it waits on, else a message that starts the idle
/// thread or steers the running turn — and 정지 interrupts its turn. 설정 > AI 'T3 Code 연결' off: nothing is asked of T3.
/// An Antigravity thread is only read, never written to (Refusal, T3Threads.Antigravity).
///
/// T3 takes an agent only at an access level someone approved, and a thread that runs in full access (most do) only
/// from an agent approved at full access. Bloom approves itself as T3's approval page does for a signed-in person:
/// a 5-minute token from T3's own command (`t3 auth session issue`, the person's local right) opens the approval and
/// says yes, the code goes for a 30-day agent token, and the 5-minute one is revoked. The agent token stays in memory
/// for this run of Bloom; the next run's first send signs in again and revokes the ones before (T3's client list keeps
/// one Bloom). No token is logged or written anywhere.
///
/// Only the sign-in waits its turn (`signing`); a call takes the signed-in agent as it is then (a T3Session snapshot)
/// and goes at once, so a send or a stop never waits behind the chat's reads. Reads go by lane (Look): one at a time
/// per lane, and a read asked while its lane is out is skipped, not queued (the poll's next tick asks again).
internal static class T3Agent
{
    /// The name T3's client list shows for Bloom, and the label of the 5-minute sign-in.
    internal const string ClientName = "Bloom 작업 카드", SignInLabel = "Bloom sign-in";

    /// Where T3 would send the browser back; nothing listens there: the code is read from the approval's answer.
    private const string Redirect = "http://127.0.0.1/bloom-run-card";

    // what the approval needs of the person's session: approving (access:write) at full access (the rest)
    private static readonly string[] SignInScopes = { "access:write", "orchestration:read", "orchestration:operate", "settings:write", "source-control:write" };

    private static readonly SemaphoreSlim signing = new(1, 1);
    private static readonly HttpClient http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };

    /// This run's mark in its sessions' user agent: the cleanup keeps the session that carries it.
    private static readonly string Agent = "Bloom/" + (typeof(T3Agent).Assembly.GetName().Version?.ToString(3) ?? "0") + " run-" + Guid.NewGuid().ToString("N")[..12];

    /// The signed-in agent: T3's address, the agent token (memory only), its MCP session, and when to sign in again.
    private sealed record T3Session(string Origin, string Token, string Mcp, DateTime Until);

    private static volatile T3Session? current;
    private static int rpc;

    /// The read lanes out now (Look).
    private static readonly HashSet<string> lanes = new();

    private static string T3Home => Path.Combine(Paths.Home, ".t3");
    private static string Cli => Path.Combine(T3Home, "bin", "t3.cmd");

    /// What a send or a stop came to: `How` is answered | steered | started | queued | stopped when `Ok`, else why it
    /// didn't go: t3-off (T3 isn't running, or 설정 > AI 'T3 Code 연결' is off), no-thread (T3 keeps no thread of the
    /// session), no-question (choices picked for a question that is no longer asked), view-only or unchecked (Refusal),
    /// failed.
    public sealed record Sent(bool Ok, string How);

    /// Why a write was not sent (Refusal): the thread or the model is Antigravity's (T3Threads.Antigravity), or T3's
    /// state can't tell what the thread runs on.
    public const string ViewOnly = "view-only", Unchecked = "unchecked";

    /// What one of T3's tools gave: its result, or why there is none: busy (Look: its lane was out), t3-off, failed.
    public sealed record Got(JsonNode? Node, string Why)
    {
        public bool Ok => Node is not null;
    }

    /// 설정 > AI 'T3 Code 연결': off, Bloom asks T3 nothing.
    private static bool On => Settings.Current.T3Link;

    /// T3 Code is on this PC with its command to sign in with, and the switch is on (it may not be running).
    public static bool Present => On && Installed();

    /// T3's state as read here: the thread a session runs in (T3Threads.Thread), whether a thread is on Antigravity
    /// (T3Threads.AntigravityThread; null: can't tell), the running T3's address (RuntimeOrigin; null: not running) and
    /// whether T3's command is there. A test's stand-in may take their place, so a test never reaches a real T3.
    internal static Func<string, string?> ThreadOf = T3Threads.Thread;
    internal static Func<string, bool?> AntigravityThreadOf = T3Threads.AntigravityThread;
    internal static Func<string?> OriginOf = RuntimeOrigin;
    internal static Func<bool> Installed = () => File.Exists(Cli);

    /// T3 runs `session` in a thread Bloom could send to and stop (T3's command is there to sign in with); never one
    /// on Antigravity.
    public static bool Can(string session) => Present && ThreadOf(session) is { } thread && Barred(thread) is null;

    /// Why Bloom sends nothing to the thread `session` runs in (ViewOnly, Unchecked); null when T3 runs it in none, or
    /// in one Bloom may write to.
    public static string? BarredSession(string session) => On && ThreadOf(session) is { } thread ? Barred(thread) : null;

    /// Why Bloom sends nothing to T3's thread `thread`; null when it may.
    private static string? Barred(string thread) => Refusal("t3_thread_send", new JsonObject { ["threadId"] = thread });

    /// Why a call of T3's tool `tool` with `arguments` is not sent; null when it may go. Reads always may. A write goes
    /// to no Antigravity thread and onto no Antigravity model (T3Threads.Antigravity), nor where
    /// T3's state can't tell (a launch on T3's own default model included).
    internal static string? Refusal(string tool, JsonObject arguments) => Refusal(tool, arguments, AntigravityThreadOf, T3Threads.AntigravityInstance);

    /// Refusal with what tells an Antigravity thread (null: can't tell) and an Antigravity provider instance.
    internal static string? Refusal(string tool, JsonObject arguments, Func<string, bool?> antigravityThread, Func<string, bool> antigravityInstance)
    {
        if (tool is "t3_thread_list" or "t3_thread_read" or "t3_project_list" || (tool == "t3_pending_request" && Text(arguments["action"]) is "list" or "read")) return null;
        if (Text(arguments["threadId"]) is { } thread)
            switch (antigravityThread(thread))
            {
                case true: return ViewOnly;
                case null: return Unchecked;
            }
        if (arguments["modelSelection"] is JsonObject selection)
            return Text(selection["instanceId"]) is not { } instance ? Unchecked : antigravityInstance(instance) ? ViewOnly : null;
        return tool == "t3_thread_launch" ? Unchecked : null; // on T3's own default model: what that runs isn't known here
    }

    private static string? Text(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) && s.Length > 0 ? s : null;

    /// `text` (and `picks`, the choices picked for the question it waits on, per question) into the thread `session`
    /// runs in.
    public static Task<Sent> Say(string session, string text, IReadOnlyList<IReadOnlyList<int>>? picks, CancellationToken ct) =>
        !On ? Task.FromResult(new Sent(false, "t3-off"))
        : ThreadOf(session) is not { } thread ? Task.FromResult(new Sent(false, "no-thread"))
        : SayTo(thread, text, picks, null, ct);

    /// `text` into T3's thread `thread`: the answer to a question it waits on (as each question's own words, or the
    /// choices `picks`), else a message (`clientRequestId`: T3 takes the same send once).
    public static async Task<Sent> SayTo(string thread, string text, IReadOnlyList<IReadOnlyList<int>>? picks, string? clientRequestId, CancellationToken ct)
    {
        if (!On) return new(false, "t3-off");
        if (Barred(thread) is { } why) return new(false, why);
        if (OriginOf() is not { } origin) return new(false, "t3-off");
        return await Sending("t3 agent", origin, s => Deliver(s, thread, text, picks, clientRequestId, ct), ct);
    }

    /// The running turn of the thread `session` runs in stopped (T3: interrupted); an idle thread is left as it is and
    /// counts as stopped.
    public static Task<Sent> Stop(string session, CancellationToken ct) =>
        !On ? Task.FromResult(new Sent(false, "t3-off"))
        : ThreadOf(session) is not { } thread ? Task.FromResult(new Sent(false, "no-thread"))
        : StopThread(thread, ct);

    /// The running turn of T3's thread `thread` interrupted.
    public static async Task<Sent> StopThread(string thread, CancellationToken ct)
    {
        if (!On) return new(false, "t3-off");
        if (Barred(thread) is { } why) return new(false, why);
        if (OriginOf() is not { } origin) return new(false, "t3-off");
        return await Sending("t3 agent stop", origin, async s =>
        {
            await Tool(s, "t3_thread_interrupt", new JsonObject { ["threadId"] = thread }, ct);
            return new Sent(true, "stopped");
        }, ct);
    }

    /// The answers given on Bloom's own question card (NoticeQuestionsView: by each question's text) to the question the
    /// thread `session` runs in waits on. no-question when it waits on none, or on another than the card showed.
    public static Task<Sent> Answer(string session, IReadOnlyDictionary<string, string> given, CancellationToken ct) =>
        !On ? Task.FromResult(new Sent(false, "t3-off"))
        : ThreadOf(session) is not { } thread ? Task.FromResult(new Sent(false, "no-thread"))
        : AnswerTo(thread, given, ct);

    /// The answers by each question's text to the question T3's thread `thread` waits on.
    public static async Task<Sent> AnswerTo(string thread, IReadOnlyDictionary<string, string> given, CancellationToken ct)
    {
        if (!On) return new(false, "t3-off");
        if (Barred(thread) is { } why) return new(false, why);
        if (OriginOf() is not { } origin) return new(false, "t3-off");
        return await Sending("t3 agent answer", origin, async s =>
        {
            if (await Pending(s, thread, ct) is not { } read || ByText(read, given) is not { } answers) return new Sent(false, "no-question");
            if (!On) throw new Off();
            await Tool(s, "t3_pending_request", new JsonObject { ["action"] = "respond", ["threadId"] = thread, ["requestId"] = read["requestId"]?.GetValue<string>(), ["answers"] = answers }, ct);
            return new Sent(true, "answered");
        }, ct);
    }

    /// `work` signed in; a failure is logged under `what` and comes back as t3-off (T3 not reached) or failed.
    private static async Task<Sent> Sending(string what, string origin, Func<T3Session, Task<Sent>> work, CancellationToken ct)
    {
        try
        {
            return await Signed(origin, work, ct);
        }
        catch (Off)
        {
            return new(false, "t3-off");
        }
        catch (NotSent n)
        {
            Log.Write(what + ": not sent, " + n.Why);
            return new(false, n.Why);
        }
        catch (Exception e) when (Failure(e))
        {
            Log.Write(what + ": " + e.GetType().Name + " " + e.Message); // messages carry no token: T3's errors and Bloom's own
            return new(false, Unreached(e) ? "t3-off" : "failed");
        }
    }

    /// The question the thread `session` runs in waits on, its choices with it; null when there is none, T3 isn't
    /// reached, or Bloom sends that thread nothing (its question is not Bloom's to answer).
    public static async Task<IReadOnlyList<CardQuestion>?> Question(string session, CancellationToken ct)
    {
        if (!On || ThreadOf(session) is not { } thread || Barred(thread) is not null) return null;
        return await QuestionOf(thread, ct) is { } asked ? asked.Select(q => q.Question).ToList() : null;
    }

    /// The question T3's thread `thread` waits on: its request id with T3's id and Bloom's card form of each question;
    /// null when it waits on none or T3 isn't reached.
    public static async Task<(string RequestId, List<(string Id, CardQuestion Question)> Questions)?> PendingOf(string thread, CancellationToken ct)
    {
        if (!On || OriginOf() is not { } origin) return null;
        try
        {
            return await Signed<(string, List<(string, CardQuestion)>)?>(origin, async s =>
                await Pending(s, thread, ct) is { } read && read["requestId"]?.GetValue<string>() is { } id ? (id, Read(read)) : null, ct);
        }
        catch (Off)
        {
            return null;
        }
        catch (Exception e) when (Failure(e))
        {
            Log.Write("t3 agent question: " + e.GetType().Name + " " + e.Message);
            return null;
        }
    }

    private static async Task<List<(string Id, CardQuestion Question)>?> QuestionOf(string thread, CancellationToken ct) =>
        await PendingOf(thread, ct) is { } p && p.Questions.Count > 0 ? p.Questions : null;

    /// One of T3's tools to look with (t3_thread_list, t3_thread_read, t3_project_list), in `lane`: busy at once while
    /// that lane's last look is still out.
    public static async Task<Got> Look(string lane, string tool, JsonObject arguments, CancellationToken ct)
    {
        lock (lanes)
            if (!lanes.Add(lane)) return new(null, "busy");
        try
        {
            return await Call("t3 look " + tool, tool, arguments, ct);
        }
        finally
        {
            lock (lanes) lanes.Remove(lane);
        }
    }

    /// One of T3's tools that changes something (t3_thread_launch): never waits behind a look.
    public static Task<Got> Act(string tool, JsonObject arguments, CancellationToken ct) => Call("t3 act " + tool, tool, arguments, ct);

    private static async Task<Got> Call(string what, string tool, JsonObject arguments, CancellationToken ct)
    {
        if (!On) return new(null, "t3-off");
        if (Refusal(tool, arguments) is { } why) return new(null, why);
        if (OriginOf() is not { } origin) return new(null, "t3-off");
        try
        {
            return new(await Signed(origin, s => Tool(s, tool, arguments, ct), ct), "");
        }
        catch (Off)
        {
            return new(null, "t3-off");
        }
        catch (NotSent n)
        {
            Log.Write(what + ": not sent, " + n.Why);
            return new(null, n.Why);
        }
        catch (Exception e) when (Failure(e))
        {
            Log.Write(what + ": " + e.GetType().Name + " " + e.Message);
            return new(null, Unreached(e) ? "t3-off" : "failed");
        }
    }

    private static bool Failure(Exception e) => e is HttpRequestException or TaskCanceledException or IOException or JsonException or Refused or InvalidOperationException or System.ComponentModel.Win32Exception;

    /// T3 isn't listening there (not running, or gone since its runtime file was written).
    private static bool Unreached(Exception e) => e is HttpRequestException { StatusCode: null };

    /// `work` with the agent signed in: once more from a new sign-in when T3 turns the agent away.
    private static async Task<T> Signed<T>(string origin, Func<T3Session, Task<T>> work, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            // the switch turned off while this waited (or before a sign-in again): nothing more goes to T3
            if (!On) throw new Off();
            var s = await Session(origin, ct);
            try
            {
                return await work(s);
            }
            catch (Refused) when (attempt == 0)
            {
                // T3 forgot the agent (revoked, restarted with new keys) or its MCP session: the next call signs in again
                Interlocked.CompareExchange(ref current, null, s);
            }
        }
    }

    /// The signed-in agent for `origin`: the one there is, else a sign-in (one at a time; a call that waited for it
    /// takes the agent the one before it signed in).
    private static async Task<T3Session> Session(string origin, CancellationToken ct)
    {
        if (Usable(current, origin) is { } now) return now;
        await signing.WaitAsync(ct);
        try
        {
            if (Usable(current, origin) is { } signed) return signed;
            if (!On) throw new Off();
            current = null;
            var (token, until) = await SignIn(origin, ct);
            string mcp = await Initialize(origin, token, ct);
            return current = new T3Session(origin, token, mcp, until);
        }
        finally
        {
            signing.Release();
        }
    }

    private static T3Session? Usable(T3Session? s, string origin) => s is not null && s.Origin == origin && DateTime.UtcNow < s.Until ? s : null;

    /// The running T3's address from its runtime file (null when T3 isn't running).
    private static string? RuntimeOrigin()
    {
        try
        {
            string file = Path.Combine(T3Home, "userdata", "server-runtime.json");
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("origin", out var o) && o.ValueKind == JsonValueKind.String && Uri.TryCreate(o.GetString(), UriKind.Absolute, out var u) && u.IsLoopback
                ? u.GetLeftPart(UriPartial.Authority) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // ---- signing in ----

    /// The agent token, and when to sign in again.
    private static async Task<(string Token, DateTime Until)> SignIn(string origin, CancellationToken ct)
    {
        var args = new List<string> { "auth", "session", "issue", "--ttl", "5m", "--label", SignInLabel, "--json" };
        foreach (string s in SignInScopes) args.AddRange(new[] { "--scope", s });
        using var issued = JsonDocument.Parse(await Run(args, ct));
        string boot = Str(issued.RootElement, "token") ?? throw new InvalidOperationException("t3 issued no token");
        string? bootId = Str(issued.RootElement, "sessionId");
        try
        {
            var auth = new Dictionary<string, string> { ["Authorization"] = "Bearer " + boot };
            using var registered = JsonDocument.Parse(await Post(origin, "/oauth/mcp/register", Json(new JsonObject { ["client_name"] = ClientName, ["redirect_uris"] = new JsonArray(Redirect) }), null, ct));
            string client = Str(registered.RootElement, "client_id") ?? throw new InvalidOperationException("t3 registered no client");
            string verifier = Base64Url(RandomNumberGenerator.GetBytes(32)), state = Base64Url(RandomNumberGenerator.GetBytes(12));
            var authorization = new JsonObject
            {
                ["response_type"] = "code",
                ["client_id"] = client,
                ["redirect_uri"] = Redirect,
                ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
                ["code_challenge_method"] = "S256",
                ["state"] = state,
                ["resource"] = origin + "/mcp",
            };
            using var approval = JsonDocument.Parse(await Post(origin, "/oauth/mcp/approval", Json(authorization), auth, ct));
            string csrf = Str(approval.RootElement, "csrfToken") ?? throw new InvalidOperationException("t3 offered no approval to this sign-in");
            var decision = new JsonObject { ["authorization"] = authorization.DeepClone(), ["decision"] = new JsonObject { ["_tag"] = "browser-session", ["access"] = "full-access", ["csrfToken"] = csrf } };
            using var decided = JsonDocument.Parse(await Post(origin, "/oauth/mcp/decision", Json(decision), auth, ct));
            string code = CodeFrom(Str(decided.RootElement, "redirectTo"), state) ?? throw new InvalidOperationException("t3 approved with no code");
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = Redirect,
                ["client_id"] = client,
                ["code_verifier"] = verifier,
                ["resource"] = origin + "/mcp",
            });
            using var got = JsonDocument.Parse(await Post(origin, "/oauth/mcp/token", form, null, ct));
            string token = Str(got.RootElement, "access_token") ?? throw new InvalidOperationException("t3 gave no agent token");
            int life = got.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int s) ? s : 86400;
            return (token, DateTime.UtcNow.AddSeconds(Math.Max(60, life - 3600))); // an hour early, never at its edge
        }
        finally
        {
            // the 5-minute token goes now, and with it the sessions of Bloom's runs before
            _ = Task.Run(() => Tidy(bootId), CancellationToken.None); // even when the sign-in was cancelled
        }
    }

    /// Revokes the sign-in `bootId` and every agent session of Bloom's but this run's (T3's client list keeps one).
    private static async Task Tidy(string? bootId)
    {
        try
        {
            var gone = new List<string>();
            if (bootId is not null) gone.Add(bootId);
            using (var listed = JsonDocument.Parse(await Run(new[] { "auth", "session", "list", "--json" }, CancellationToken.None)))
                gone.AddRange(Stale(listed.RootElement, Agent).Where(id => id != bootId));
            foreach (string id in gone) await Run(new[] { "auth", "session", "revoke", id }, CancellationToken.None);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            Log.Write("t3 agent tidy: " + e.Message);
        }
    }

    /// The sessions in `t3 auth session list --json` that are Bloom's (its sign-ins, its agents) and not this run's,
    /// whose user agent is `agent`.
    internal static IEnumerable<string> Stale(JsonElement listed, string agent)
    {
        var rows = listed.ValueKind == JsonValueKind.Array ? listed : listed.TryGetProperty("sessions", out var s) && s.ValueKind == JsonValueKind.Array ? s : default;
        if (rows.ValueKind != JsonValueKind.Array) yield break;
        foreach (var row in rows.EnumerateArray())
        {
            if (Str(row, "sessionId") is not { } id || !row.TryGetProperty("client", out var c) || c.ValueKind != JsonValueKind.Object) continue;
            string? label = Str(c, "label");
            if (label == SignInLabel || (label == ClientName && Str(c, "userAgent") != agent)) yield return id;
        }
    }

    /// The code in the approval's way back, when its state is this sign-in's.
    internal static string? CodeFrom(string? redirectTo, string state)
    {
        if (redirectTo is null || !Uri.TryCreate(redirectTo, UriKind.Absolute, out var u)) return null;
        string? code = null, back = null;
        foreach (string part in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string name = part[..eq], value = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            if (name == "code") code = value;
            else if (name == "state") back = value;
        }
        return back == state && code is { Length: > 0 } ? code : null;
    }

    /// T3's command (`t3.cmd`, which T3 keeps pointing at its own copy) with `args`, no window; its output.
    private static async Task<string> Run(IEnumerable<string> args, CancellationToken ct)
    {
        // cmd /s /c "…": the quotes around the whole line go, the rest stays as written (the arguments are Bloom's own words)
        string line = "/d /s /c \"\"" + Cli + "\" " + string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a)) + "\"";
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), line)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = ChildProcess.Spawn(psi) ?? throw new InvalidOperationException("t3 did not start");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var output = p.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = p.StandardError.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            string said = await output;
            if (p.ExitCode != 0) throw new InvalidOperationException($"t3 {args.First()} {args.Skip(1).FirstOrDefault()} exited {p.ExitCode}: {OneShot.Short(await error)}");
            return said;
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch (InvalidOperationException) { /* gone */ }
            throw;
        }
    }

    private static async Task<string> Post(string origin, string path, HttpContent content, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, origin + path) { Content = content };
        req.Headers.TryAddWithoutValidation("User-Agent", Agent);
        if (headers is not null)
            foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
        using var res = await http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new Refused($"{path} {(int)res.StatusCode}");
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"{path} {(int)res.StatusCode}: {OneShot.Short(body)}");
        return body;
    }

    // ---- T3's tools ----

    private static async Task<string> Initialize(string origin, string token, CancellationToken ct)
    {
        var (_, session) = await Mcp(origin, token, null, "initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "bloom-run-card", ["version"] = typeof(T3Agent).Assembly.GetName().Version?.ToString(3) ?? "0" },
        }, ct);
        string id = session ?? "";
        await Mcp(origin, token, id, "notifications/initialized", null, ct);
        return id;
    }

    private static async Task<Sent> Deliver(T3Session s, string thread, string text, IReadOnlyList<IReadOnlyList<int>>? picks, string? clientRequestId, CancellationToken ct)
    {
        // a question the thread waits on takes the choices picked and the words typed as its answers (T3: "Other")
        if (await Pending(s, thread, ct) is { } read)
        {
            if (!On) throw new Off();
            await Tool(s, "t3_pending_request", new JsonObject { ["action"] = "respond", ["threadId"] = thread, ["requestId"] = read["requestId"]?.GetValue<string>(), ["answers"] = Answers(read, text, picks) }, ct);
            return new(true, "answered");
        }
        if (picks is not null) return new(false, "no-question"); // answered elsewhere meanwhile: the picks mean nothing now
        if (!On) throw new Off();
        var send = new JsonObject { ["threadId"] = thread, ["message"] = text, ["mode"] = "auto" };
        if (clientRequestId is not null) send["clientRequestId"] = clientRequestId;
        var sent = await Tool(s, "t3_thread_send", send, ct);
        return new(true, HowSent(sent["delivery"]?.GetValue<string>()));
    }

    /// The question request the thread waits on, read (its id and its questions); null when it waits on none.
    private static async Task<JsonNode?> Pending(T3Session s, string thread, CancellationToken ct)
    {
        var pending = await Tool(s, "t3_pending_request", new JsonObject { ["action"] = "list", ["threadId"] = thread }, ct);
        if (pending["requestIds"] is not JsonArray { Count: > 0 } ids || ids[0]?.GetValue<string>() is not { } request) return null;
        var read = await Tool(s, "t3_pending_request", new JsonObject { ["action"] = "read", ["threadId"] = thread, ["requestId"] = request }, ct);
        read["requestId"] ??= request;
        return read;
    }

    /// A read question request's questions: T3's id for each, and the question as Bloom's cards show one.
    internal static List<(string Id, CardQuestion Question)> Read(JsonNode read)
    {
        var list = new List<(string, CardQuestion)>();
        if (read["questions"] is not JsonArray questions) return list;
        foreach (var q in questions)
        {
            if (q?["id"]?.GetValue<string>() is not { Length: > 0 } id) continue;
            var choices = q["options"] is JsonArray options
                ? options.Select(o => new CardChoice(o?["label"]?.GetValue<string>() ?? "", o?["description"]?.GetValue<string>() ?? "")).Where(c => c.Label.Length > 0).ToList()
                : new List<CardChoice>();
            list.Add((id, new CardQuestion(q["question"]?.GetValue<string>() ?? id, q["header"]?.GetValue<string>() ?? "", choices, q["multiSelect"]?.GetValue<bool>() == true)));
        }
        return list;
    }

    /// The answers T3 takes for a read question request, by question id: the choices picked for it (`picks`, in the
    /// questions' order), else the words typed, as Bloom's own card joins them. A question left with neither throws.
    internal static JsonObject Answers(JsonNode read, string text, IReadOnlyList<IReadOnlyList<int>>? picks)
    {
        var answers = new JsonObject();
        var questions = Read(read);
        for (int i = 0; i < questions.Count; i++)
        {
            var (id, q) = questions[i];
            IReadOnlyList<int> picked = picks is not null && i < picks.Count ? picks[i] : [];
            string answer = CardQuestions.Joined(q, picked, text);
            if (answer.Length == 0) throw new InvalidOperationException("a question left unanswered");
            answers[id] = answer;
        }
        if (answers.Count == 0) throw new InvalidOperationException("t3 showed a question request with no questions");
        return answers;
    }

    /// The answers T3 takes for a read question request, by question id, from answers given by each question's text;
    /// null when a question of it has none (it is not the one they were given for).
    internal static JsonObject? ByText(JsonNode read, IReadOnlyDictionary<string, string> given)
    {
        var answers = new JsonObject();
        foreach (var (id, q) in Read(read))
        {
            if (!given.TryGetValue(q.Text, out string? answer) || answer.Length == 0) return null;
            answers[id] = answer;
        }
        return answers.Count > 0 ? answers : null;
    }

    /// How T3 took a send (its `delivery`): steered into the running turn, started the idle thread, queued.
    internal static string HowSent(string? delivery) => delivery switch
    {
        "steered" => "steered",
        "started" => "started",
        _ => "queued",
    };

    /// One of T3's tools: its result's structured content (or its text as JSON); a tool error throws, and so does a
    /// write Refusal bars (every call to T3 comes through here: nothing barred is sent, whoever asks).
    private static async Task<JsonNode> Tool(T3Session s, string name, JsonObject arguments, CancellationToken ct)
    {
        if (Refusal(name, arguments) is { } why) throw new NotSent(why);
        var (result, _) = await Mcp(s.Origin, s.Token, s.Mcp, "tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments }, ct);
        return ToolResult(result ?? throw new InvalidOperationException(name + ": no result"), name);
    }

    internal static JsonNode ToolResult(JsonNode result, string name)
    {
        bool error = result["isError"]?.GetValue<bool>() == true;
        JsonNode? content = result["structuredContent"];
        string? text = result["content"] is JsonArray { Count: > 0 } c ? c[0]?["text"]?.GetValue<string>() : null;
        if (error) throw new InvalidOperationException(name + ": " + OneShot.Short(text ?? content?.ToJsonString() ?? "error"));
        if (content is not null) return content;
        return text is not null ? JsonNode.Parse(text) ?? new JsonObject() : new JsonObject();
    }

    /// A JSON-RPC call (`params` null and no id for a notification) to T3's MCP endpoint: its result, and the MCP session
    /// the answer names.
    private static async Task<(JsonNode? Result, string? Session)> Mcp(string origin, string token, string? session, string method, JsonObject? @params, CancellationToken ct)
    {
        bool notice = method.StartsWith("notifications/", StringComparison.Ordinal);
        int id = Interlocked.Increment(ref rpc);
        var call = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (!notice) call["id"] = id;
        if (@params is not null) call["params"] = @params;
        using var req = new HttpRequestMessage(HttpMethod.Post, origin + "/mcp") { Content = Json(call) };
        req.Headers.TryAddWithoutValidation("User-Agent", Agent);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        req.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        if (!string.IsNullOrEmpty(session)) req.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
        using var res = await http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        // 401/403: the agent token is gone; 404: T3 forgot the MCP session (it says so for an unknown one)
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound) throw new Refused($"mcp {method} {(int)res.StatusCode}");
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"mcp {method} {(int)res.StatusCode}: {OneShot.Short(body)}");
        string? named = res.Headers.TryGetValues("Mcp-Session-Id", out var v) ? v.FirstOrDefault() : null;
        if (notice) return (null, named);
        return (Reply(body, id), named);
    }

    /// The result of call `id` in an MCP answer: JSON, or an event stream whose `data:` lines carry it. An error throws.
    internal static JsonNode Reply(string body, int id)
    {
        IEnumerable<string> messages = body.TrimStart().StartsWith('{')
            ? new[] { body }
            : body.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].Trim());
        foreach (string m in messages)
        {
            if (JsonNode.Parse(m) is not JsonObject o || o["id"]?.GetValueKind() != JsonValueKind.Number || o["id"]!.GetValue<int>() != id) continue;
            if (o["error"] is { } err) throw new InvalidOperationException("mcp error: " + OneShot.Short(err["message"]?.GetValue<string>() ?? err.ToJsonString()));
            return o["result"] ?? throw new InvalidOperationException("mcp answer with no result");
        }
        throw new InvalidOperationException("mcp gave no answer to call " + id);
    }

    private static StringContent Json(JsonNode o) => new(o.ToJsonString(), Encoding.UTF8, "application/json");

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// T3 turned the agent or its session away: sign in again.
    private sealed class Refused(string what) : Exception(what);

    /// 설정 > AI 'T3 Code 연결' went off while a call waited or ran: it ends as t3-off, asking T3 nothing more.
    private sealed class Off() : Exception("t3 link off");

    /// A write Refusal barred: it ends as `Why`, nothing of it sent.
    private sealed class NotSent(string why) : Exception("not sent: " + why)
    {
        public string Why { get; } = why;
    }
}
