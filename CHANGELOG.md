# Changelog

## 2026-09-16: Jobs survive a rolling deploy
When a worker process meets a scheduled job whose class it cannot load — normally because it is running an older build partway through a deploy — it now leaves the job in place for five minutes instead of deleting it straight away, so a peer process on a newer build can pick it up. If nothing manages to run the job within that window, it is deleted and a single warning names it. Scheduling a job again under the same name reopens the window and updates the stored job class, which it previously did not.
