# Update dialog layout regression

Run on Windows with the .NET 10 SDK:

```powershell
dotnet run --project tests/Update.DialogLayoutTests -c Release
```

The STA executable instantiates the application resources and actual `UpdateDialog` XAML, then measures and arranges its content without showing a window or running application startup. It checks short and long release notes at 480/600 DIP widths, minimum and intermediate heights, shrinking and expanding, long error text, scrolling, and the download cancellation button. Client-height calculations reserve the system caption and resize borders; the content grid applies its own margin.

Download delegates are controlled in-memory tasks. The cancellation test raises the actual button event and checks the passed token. No network request, updater, installed launcher, or game is started, and no user settings are saved.

These are native WPF layout and event checks. They do not verify screenshots, monitor-specific window placement, or a real download/install session.

The same executable also measures the actual SettingsWindow with a supplied hardware fixture at 1160/850 DIP widths. It checks the two 474-DIP column/card bottom edges, authored MFG and maintenance content bounds, and all three equally sized maintenance buttons. Only isolated test-output settings/log directories are used; hardware detection and maintenance commands are not executed.
