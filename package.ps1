Remove-Item -Recurse -Force dist\temp -ErrorAction Ignore
New-Item -ItemType Directory -Path dist\temp\BepInEx\plugins\HorusMod -Force
Copy-Item "bin\Debug\netstandard2.1\HorusMod.dll" -Destination dist\temp\BepInEx\plugins\HorusMod\HorusMod.dll -Force
Copy-Item "README.md" -Destination dist\temp\README.md -Force
Copy-Item "CHANGELOG.md" -Destination dist\temp\CHANGELOG.md -Force
Compress-Archive -Path dist\temp\* -DestinationPath dist\Horus_Mod_Starter_v1.2.1_test.zip -Force
Remove-Item -Recurse -Force dist\temp -ErrorAction Ignore
