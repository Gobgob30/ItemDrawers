# <Exec Command=":loop &amp; tasklist | findstr /i valheim.exe &gt;nul 2&gt;&amp;1 &amp; if errorlevel 1 (xcopy /Y &quot;$(TargetDir)$(TargetName)$(TargetExt)&quot; &quot;$(ThunderstorePath)$(PluginPath)$(TargetName)\&quot; &amp; exit /b) else (timeout /t 1 &amp; goto loop)" ContinueOnError="true">
# Check if the game is running, if it is, wait 1 second and try again
# If the game is not running, copy the plugin to the game folder

# Path: onBuild.ps1

$sourcePath = $args[0]
$destinationPath = $args[1]
if ($null -eq $sourcePath -or $null -eq $destinationPath) {
        Write-Output "Missing arguments"
        exit 1
}

$ErrorActionPreference = "SilentlyContinue"
$process = Get-Process "valheim" -ErrorAction SilentlyContinue

while ($null -ne $process) {
        Start-Sleep -Seconds 1
        $process = Get-Process "valheim" -ErrorAction SilentlyContinue
}

Copy-Item -Path $sourcePath -Destination $destinationPath -Force #-PassThru | Write-Output

