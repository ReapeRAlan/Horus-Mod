Remove-Item -Recurse -Force dist\temp -ErrorAction Ignore
New-Item -ItemType Directory -Path dist\temp\BepInEx\plugins\HorusMod -Force
Copy-Item "bin\Release\netstandard2.1\HorusMod.dll" -Destination dist\temp\BepInEx\plugins\HorusMod\HorusMod.dll -Force
Copy-Item "README.md" -Destination dist\temp\README.md -Force
Copy-Item "CHANGELOG.md" -Destination dist\temp\CHANGELOG.md -Force
Copy-Item "ROADMAP.md" -Destination dist\temp\ROADMAP.md -Force
New-Item -ItemType Directory -Path dist\temp\docs -Force
Copy-Item "docs\BepInEx.dev.cfg" -Destination dist\temp\docs\BepInEx.dev.cfg -Force
Compress-Archive -Path dist\temp\* -DestinationPath dist\Horus_Mod_Starter_v1.4.3.zip -Force
Remove-Item -Recurse -Force dist\temp -ErrorAction Ignore
