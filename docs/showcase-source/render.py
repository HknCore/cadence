import asyncio, sys
from playwright.async_api import async_playwright
OUT="/home/claude/cadence/docs/images/"
IDS=["hero","pacing","modes","overlay","app-overview","app-profiles","app-stats","tray"]
async def main():
    async with async_playwright() as p:
        b=await p.chromium.launch(executable_path="/opt/pw-browsers/chromium-1194/chrome-linux/chrome")
        pg=await b.new_page(viewport={"width":1400,"height":900},device_scale_factor=1.5)
        msgs=[]; pg.on("console",lambda m: msgs.append(m.text)); pg.on("pageerror",lambda e: msgs.append(str(e)))
        await pg.goto("file://"+sys.argv[1]); await pg.evaluate("document.fonts.ready"); await pg.wait_for_timeout(300)
        for i in IDS:
            await pg.locator("#"+i).screenshot(path=OUT+i+".png",omit_background=True)
        print("errors:",msgs)
        await b.close()
asyncio.run(main())
