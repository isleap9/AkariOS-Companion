# Remove Unwanted Pre-Installed Apps
$apps = @(
    "Microsoft.BingNews","Microsoft.BingWeather","Microsoft.BingFinance","Microsoft.BingSports",
    "Microsoft.GamingApp","Microsoft.GetHelp","Microsoft.Getstarted","Microsoft.Messaging",
    "Microsoft.MicrosoftOfficeHub","Microsoft.MicrosoftSolitaireCollection","Microsoft.MixedReality.Portal",
    "Microsoft.OneConnect","Microsoft.People","Microsoft.Print3D","Microsoft.SkypeApp",
    "Microsoft.Todos","Microsoft.WindowsFeedbackHub","Microsoft.WindowsMaps",
    "Microsoft.WindowsSoundRecorder","Microsoft.Xbox.TCUI","Microsoft.XboxApp",
    "Microsoft.XboxGameOverlay","Microsoft.XboxIdentityProvider","Microsoft.XboxSpeechToTextOverlay",
    "Microsoft.YourPhone","Microsoft.ZuneMusic","Microsoft.ZuneVideo","MicrosoftTeams",
    "Clipchamp.Clipchamp","Microsoft.549981C3F5F10"
)
foreach ($app in $apps) {
    Get-AppxPackage -Name $app -AllUsers | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue
    Get-AppxProvisionedPackage -Online | Where-Object DisplayName -like $app | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue
    Write-Host "Removed $app"
}
Write-Host "Debloat complete."