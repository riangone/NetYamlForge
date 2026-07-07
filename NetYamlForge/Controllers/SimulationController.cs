using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

[Authorize(AuthenticationSchemes = $"{Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme},ApiToken")]
[Route("Simulation")]
public class SimulationController : Controller
{
    private const string LogDir = "/home/ubuntu/ws/NetYamlForge/logs/experiments";
    private static readonly string SimulationLogPath = Path.Combine(LogDir, "simulation.jsonl");
    private static readonly string FindingsLogPath = Path.Combine(LogDir, "findings.jsonl");
    private static readonly string SchedulerLogPath = Path.Combine(LogDir, "scheduler.log");

    [HttpGet("")]
    public IActionResult Index()
    {
        var isRunning = CheckIsRunning();
        var logs = ReadSimulationLogs(30);
        var findings = ReadFindingsLogs(30);

        ViewBag.IsRunning = isRunning;
        ViewBag.Logs = logs;
        ViewBag.Findings = findings;
        ViewBag.SchedulerLog = ReadSchedulerLog();

        return View();
    }

    [HttpPost("Start")]
    public IActionResult Start()
    {
        if (!CheckIsRunning())
        {
            Directory.CreateDirectory(LogDir);
            RunShellCommand($"nohup python3 -u /home/ubuntu/ws/NetYamlForge/scripts/experiments/schedule_simulation.py > {SchedulerLogPath} 2>&1 &");
            TempData["Message"] = "Simulation started successfully.";
        }
        else
        {
            TempData["Message"] = "Simulation is already running.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Stop")]
    public IActionResult Stop()
    {
        if (CheckIsRunning())
        {
            RunShellCommand("pkill -f schedule_simulation.py");
            TempData["Message"] = "Simulation stopped.";
        }
        else
        {
            TempData["Message"] = "Simulation is not running.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("ClearLogs")]
    public IActionResult ClearLogs()
    {
        try
        {
            if (System.IO.File.Exists(SimulationLogPath)) System.IO.File.Delete(SimulationLogPath);
            if (System.IO.File.Exists(FindingsLogPath)) System.IO.File.Delete(FindingsLogPath);
            if (System.IO.File.Exists(SchedulerLogPath)) System.IO.File.Delete(SchedulerLogPath);
            TempData["Message"] = "Logs cleared successfully.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to clear logs: {ex.Message}";
        }
        return RedirectToAction(nameof(Index));
    }

    private static bool CheckIsRunning()
    {
        var output = RunShellCommand("pgrep -f schedule_simulation.py");
        return !string.IsNullOrEmpty(output);
    }

    private static string ReadSchedulerLog()
    {
        if (!System.IO.File.Exists(SchedulerLogPath)) return "No scheduler logs found.";
        try
        {
            var lines = System.IO.File.ReadLines(SchedulerLogPath).TakeLast(50);
            return string.Join(Environment.NewLine, lines);
        }
        catch
        {
            return "Error reading scheduler log.";
        }
    }

    private static List<SimulationLogEntry> ReadSimulationLogs(int limit)
    {
        var list = new List<SimulationLogEntry>();
        if (!System.IO.File.Exists(SimulationLogPath)) return list;

        try
        {
            var lines = System.IO.File.ReadLines(SimulationLogPath).TakeLast(limit * 2); // Get more to account for parse failures
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<SimulationLogEntry>(line, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (entry != null) list.Add(entry);
                }
                catch { /* Ignore corrupt line */ }
            }
        }
        catch { /* Ignore read errors */ }

        list.Reverse();
        return list.Take(limit).ToList();
    }

    private static List<FindingLogEntry> ReadFindingsLogs(int limit)
    {
        var list = new List<FindingLogEntry>();
        if (!System.IO.File.Exists(FindingsLogPath)) return list;

        try
        {
            var lines = System.IO.File.ReadLines(FindingsLogPath).TakeLast(limit * 2);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<FindingLogEntry>(line, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (entry != null) list.Add(entry);
                }
                catch { /* Ignore corrupt line */ }
            }
        }
        catch { /* Ignore read errors */ }

        list.Reverse();
        return list.Take(limit).ToList();
    }

    private static string RunShellCommand(string command)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Trim();
    }
}

public class SimulationLogEntry
{
    public string Timestamp { get; set; } = "";
    public string User { get; set; } = "";
    public string Role { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public object? RequestPayload { get; set; }
    public int StatusCode { get; set; }
    public object? ResponsePayload { get; set; }
    public int DurationMs { get; set; }
    public string? Error { get; set; }
    public List<InvariantViolation> InvariantViolations { get; set; } = new();
}

public class InvariantViolation
{
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
}

public class FindingLogEntry
{
    public string Timestamp { get; set; } = "";
    public string User { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public object? Payload { get; set; }
    public string RunnerOutput { get; set; } = "";
}
