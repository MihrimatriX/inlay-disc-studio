# 0004 — "Disk kopyala" is in scope as a data-disc copy

**Status:** accepted (M1, user decision)

`app-prompt.md` §1 lists "disk kopyalama/rip" as out of scope, while §11 and `design-prompt.md` §3.1 show
"Disk kopyala" in the sidebar. The user chose to keep the tool. Scope is deliberately narrow: a **data** disc
(ISO9660/Joliet/UDF) is read sector by sector into an image and burned to a new blank disc. No audio CD ripping,
no DVD-Video, no copy-protection handling — those remain out of scope as §1 says.
