using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EfmlGen.Vsix.Services
{
    /// <summary>
    /// Spawns <c>EfmlGen.Cli.exe</c> as a subprocess and streams stdout/stderr asynchronously.
    /// Decouples the UI thread from the CLI's runtime — every line is delivered via
    /// the supplied <see cref="IProgress{T}"/> callbacks so callers can route to an
    /// <see cref="OutputPaneLogger"/> or anywhere else without taking a hard dependency.
    /// </summary>
    internal static class CliRunner
    {
        /// <summary>
        /// Runs <c>EfmlGen.Cli.exe &lt;verb&gt; &lt;args...&gt;</c> and returns the exit code.
        /// </summary>
        /// <param name="verb">CLI verb: <c>db-smoke</c> | <c>scaffold-efml</c> | <c>gen-code</c> | any other.</param>
        /// <param name="args">Flag/value pairs. Each entry is quoted automatically if it contains spaces.</param>
        /// <param name="onStdout">Invoked once per line of stdout. May run on a thread pool thread.</param>
        /// <param name="onStderr">Invoked once per line of stderr. May run on a thread pool thread.</param>
        /// <param name="workingDirectory">Override CWD. Default = directory of the bundled CLI.</param>
        /// <param name="cancellationToken">When cancelled, the subprocess is killed (best effort).</param>
        public static async Task<int> RunAsync(
            string verb,
            IReadOnlyList<string> args,
            IProgress<string> onStdout,
            IProgress<string> onStderr,
            string workingDirectory = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(verb)) throw new ArgumentException("verb is required", nameof(verb));

            var argsLine = BuildArgsLine(verb, args ?? Array.Empty<string>());
            var psi = new ProcessStartInfo
            {
                FileName = CliLocator.ExePath,
                Arguments = argsLine,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workingDirectory ?? System.IO.Path.GetDirectoryName(CliLocator.ExePath),
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) onStdout?.Report(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) onStderr?.Report(e.Data);
            };

            var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (_, _) => exitTcs.TrySetResult(process.ExitCode);

            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start: {psi.FileName}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill();
                }
                catch { /* race with natural exit — swallow */ }
            }))
            {
                var exitCode = await exitTcs.Task.ConfigureAwait(false);
                // Allow async readers to flush before returning.
                process.WaitForExit();
                return exitCode;
            }
        }

        private static string BuildArgsLine(string verb, IReadOnlyList<string> args)
        {
            var sb = new StringBuilder();
            sb.Append(verb);
            foreach (var arg in args)
            {
                if (arg == null) continue;
                sb.Append(' ');
                sb.Append(QuoteIfNeeded(arg));
            }
            return sb.ToString();
        }

        private static string QuoteIfNeeded(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            // Quote if contains whitespace, quotes, or backslash-quote sequences.
            var needsQuoting = false;
            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c) || c == '"') { needsQuoting = true; break; }
            }
            if (!needsQuoting) return s;
            // Escape embedded double-quotes; leave backslashes alone (Windows CLR ProcessStartInfo handles them).
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }
    }
}
