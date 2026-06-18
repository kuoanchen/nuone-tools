# Agent 備註

修改這個 repo 前，先讀 `CODEMAP.md`。它是精簡地圖，說明功能大致分布在哪裡，也能避免一開始就做範圍過大的高 token 搜尋。

請把變更範圍維持精簡，遵循既有的 WinUI partial class 模式，並且不要回復與本次任務無關的 dirty files。除非使用者有要求，或驗證這次修正真的有需要，否則不要在這個 repo 主動跑完整 build。

如果 bug 是間歇性、跨執行緒、非同步、遠端路徑、SSH、watcher，或其他難以重現的情況，請先加入聚焦的 diagnostic logging，讓失敗可以被記錄到 log 中，而不是只靠猜測排查。
