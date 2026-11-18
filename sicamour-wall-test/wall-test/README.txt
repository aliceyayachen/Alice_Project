SICAMOUR WALL BOOK - LAB PERFORMANCE TEST
=========================================

What's here
  sicamour-wall.html   the wall book (mouse simulates one floor blob for this test)
  fonts/               put EBGaramond-Regular.ttf here (see fonts/PUT-FONT-HERE.txt)

One-time setup (needs internet once, on either computer)
  1. Download EB Garamond from https://fonts.google.com/specimen/EB+Garamond
     ("Get font" -> "Download all").
  2. Unzip it and copy static/EBGaramond-Regular.ttf into the fonts folder here.
  3. Commit this whole folder (HTML + fonts) to your GitHub repo so the lab PC gets it.
     The font is under the SIL Open Font License, so it's fine to keep in the repo.
  After that, no internet is needed.

Open it
  - Double-click sicamour-wall.html, or drag it into a Chrome window.
  - Keep the HTML file and the fonts folder together in the same folder.
  - Use Chrome (that's what it was tested in).

Lab test (about 15 minutes)
  1. Start your Unity floor scene as usual on the floor monitor.
  2. Open sicamour-wall.html in Chrome, drag the window onto the monitor that mirrors
     to the WALL projector, then press F11 for full screen.
  3. Turn on the frame-rate meter: press F12 -> click the three-dot menu in DevTools ->
     More tools -> Rendering -> tick "Frame rendering stats". Close DevTools afterwards
     if the meter stays (or keep DevTools docked on another monitor).
  4. Leave both running. Every few minutes, move the mouse near and over the book, click
     the left page, then the right page within ~10 seconds (this runs the comparison).

  Pass if
  - the wall stays at about 30 fps or higher, including during reveals and the comparison
  - the floor scene's frame rate doesn't visibly drop while Chrome is running
  - the text looks like the prototype (if it looks like a plain serif, the font isn't loaded:
    check the fonts folder; F12 -> Console shows "Failed to load resource" errors in that case)
  - nothing freezes or goes black over 15 minutes

Note: in this test the mouse is a stand-in for a floor blob. The yellow light dot you see on
the wall is a testing shortcut only and won't be part of the installation.
