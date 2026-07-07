# Restore Removed Bloatware Apps - Undo
# Note: This will attempt to reinstall Windows apps from the Store
$apps = @(
    "Microsoft.BingNews",
    "Microsoft.BingWeather", 
    "Microsoft.GetHelp",
    "Microsoft.Getstarted",
    "Microsoft.MicrosoftOfficeHub",
    "Microsoft.MicrosoftSolitaireCollection",
    "Microsoft.WindowsFeedbackHub",
    "Microsoft.YourPhone"
)

foreach ($app in $apps) {
    Write-Host "Attempting to reinstall $app..."
    Get-AppxPackage -Name $app -AllUsers | Add-AppxPackage -ErrorAction SilentlyContinue
}
Write-Host "Bloatware restoration attempted."
