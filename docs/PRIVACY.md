# What FitFix does with your files

Written 2026-11-28, for the public beta. This is the English source of the note the page itself
shows in Ukrainian (`web/src/i18n/uk.ts`, the `privacy` key). Every sentence below describes the
service as it is built, and each one is traceable to a decision in
`docs/planning-artifacts/architecture/architecture-fitfix-2026-11-19/ARCHITECTURE-SPINE.md`.

## The short version

You upload two files. They are repaired in memory, the result comes back in the answer, and
nothing is kept — not your files, not the repaired one, not where you ran.

## In full

**The files.** A repair takes two: the activity from your watch and a reference track. They
travel over HTTPS, are held in memory for the length of one request, and are read by nothing
else.

**Nothing is written down.** The service writes no file at any point. Its container runs on a
read-only filesystem with no storage attached, and the upload is buffered in memory on purpose
rather than spilled to a temporary file — the size limit and the in-memory threshold are the
same number precisely so that it cannot (AD-3, AD-13). Continuous integration checks this on the
running container with every change.

**The repaired file.** It is returned in the response to your request and kept nowhere. If you
close the tab before saving it, it is gone, and the only way to get it back is to repair the two
files again.

**The logs.** One line per request: the method, the route that matched, the status, how long it
took, how many bytes the request announced, and a code if something was refused. Not your
coordinates, not your file names, not anything from inside your files, and not your IP address
(AD-9).

**Your IP address.** It is used, in memory, to count how many requests come from one place, so
that one caller cannot take the service down for everybody. The counters hold minutes' worth of
numbers, they are never written anywhere, and they disappear when the service restarts.

**No account, no cookie, no tracking.** There is nothing to sign up for. The page sets no cookie,
stores nothing in your browser beyond what the browser caches of the page itself, and carries no
analytics and no third-party scripts.

**The map.** The route is drawn in your browser. The map's background tiles come from
OpenStreetMap, which therefore sees your browser asking for the tiles that cover the area your
route is in — that is how any map on the web works. FitFix sends OpenStreetMap nothing: not your
files, not your activity, not anything about you. If you would rather it saw nothing at all,
don't open the map.

**Strava.** The service is not connected to Strava and asks for no access to it. You export your
file yourself and upload the repaired one yourself.

**Who runs it.** One person, as a side project, for a small group of testers. It is not a
company and there is no support desk.

## Questions, or something wrong

Open an issue: <https://github.com/illia-miatka/FitFix/issues>. Something wrong with the *map*
itself — a missing path, a wrong name — belongs with OpenStreetMap instead:
<https://www.openstreetmap.org/fixthemap>.

## What this note does not promise

It describes what the service does, not a legal guarantee. It is a beta: it can be switched off,
it can break, and it can lose the request you were in the middle of. Keep your original file.
