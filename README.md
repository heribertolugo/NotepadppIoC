```
Quick and dirty scanner for IoC's related to 2025/2026 Notepad++ hack.

What it does:
-Scans list of directories for known malicious file hashes, for all users
-Scans for related mutex
-Scans registry and services for known persistance 

How to use:
Run as administrator. Click scan. Thats it.
-You can skip certain scans by unchecking the corresponding item in the list.
-You can specify a specific directory to scan.
-You can choose to display all items scanned. Default only shows potencial issue items.
-You can modify, add, remove the SHA values to scan for and modify the default directories searched, by changing the IoC.json file.
SHA1 and SHA256 values supported.

What it does not do:
-Guarantee you weren't/aren't infected/targeted.
-Check history of websites accessed from the machine
-Check cmd/powershell arguments made in the machine
-Remove/alter/edit any files/items in the system. Even if they match a malicious signature

If a scanned item could not be accessed (locked, no permissions, etc) it will be displayed in the grid.
If a scanned item is flagged suspicious or malicious it will be shown as red on the grid.
If you choose to display all scanned items, items accessed and not flagged will be shown as green.

Running as admin allows accessing files without issues, and accessing files in other user profiles, as well as scanning the registry and services on the machine and mutex.
This was created using .NET 4.8 to allow the most compatibility without the need to download external libraries (such as newer .NET versions).

Primary sources for information were:
https://securelist.com/notepad-supply-chain-attack/118708/
https://www.rapid7.com/blog/post/tr-chrysalis-backdoor-dive-into-lotus-blossoms-toolkit/
```
