namespace AkariOSCompanion.Services;

public abstract record RunAction;
public sealed record ScriptAction(string FileName) : RunAction;
public sealed record CommandAction(string Command, string? AppName = null) : RunAction;
public sealed record UrlAction(string Url) : RunAction;
