$ErrorActionPreference = 'Stop'
git config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) { throw 'Could not configure Git hooks.' }
Write-Output 'Installed repository commit-msg hook.'
