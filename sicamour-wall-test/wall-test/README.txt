SICAMOUR WALL BOOK
==================

Files
  sicamour-wall.html     the wall book. Opened directly, it waits for Unity (installation mode).
  open-test-panel.html   opens the book with on-screen sliders/buttons that imitate Unity.
  open-mouse-test.html   opens the old mouse version (pointer = blob, click = arrival).
  fonts/                 put EBGaramond-Regular.ttf here (see fonts/PUT-FONT-HERE.txt)

Three modes
  1. Installation / Unity mode  (double-click sicamour-wall.html)
     - No light dot on the book, mouse does nothing.
     - The book connects to Unity at ws://localhost:8787/book and retries every second
       until Unity is running. Until then the book just rests (ink, ribbons, glow).
     - Unity sends:  {"type":"proximity","left":0-1,"right":0-1}
                     {"type":"arrive","page":"left"|"right","x":0-1 (optional)}
       The book replies: {"type":"state","left":..,"right":..,"collator":true/false}
                         {"type":"arrived","page":..,"accepted":true/false}
       (states: idle / revealing / holding / covering; accepted=false means that page was busy)
     - Chrome's DevTools Console (F12) will show red "WebSocket connection failed" lines
       while Unity isn't running. That's just the retry - harmless.

  2. Test panel  (double-click open-test-panel.html)
     - Sliders = how close a blob is to each page's floor zone (0 far, 1 at the zone).
     - Buttons = "a blob arrived at this page".
     - Use this to try the book without Unity.

  3. Mouse test  (double-click open-mouse-test.html)
     - The previous behavior: the pointer is a blob over the book, click a page to reveal.

Font (once, needs internet)
  Download EB Garamond from https://fonts.google.com/specimen/EB+Garamond ("Get font" ->
  "Download all"), and copy static/EBGaramond-Regular.ttf into the fonts folder.
