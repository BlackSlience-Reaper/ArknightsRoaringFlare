"""原版对照数值：荣耀战斗背景 16:9 画面明度、地图纸面平均色、休息处贴图明度。"""
from pathlib import Path
from PIL import Image, ImageStat

REF = Path(__file__).resolve().parent.parent / "reference"


def mean_l(img, thresh=200):
    a = img.getchannel("A").point(lambda v: 255 if v > thresh else 0) if img.mode == "RGBA" else None
    return ImageStat.Stat(img.convert("L"), a).mean[0], ImageStat.Stat(img.convert("RGB"), a).mean


v = Image.open(REF / "refs" / "vanilla_glory_view.png").convert("RGB")
print("glory view L=%.1f" % mean_l(v)[0])
v = Image.open(REF / "refs" / "vanilla_overgrowth_view.png").convert("RGB")
print("overgrowth view L=%.1f" % mean_l(v)[0])
for act, root in (("glory", REF / "glory" / "map"), ("hive", REF / "hive")):
    tall = Image.new("RGBA", (2036, 4320))
    for i, p in enumerate(("top", "middle", "bottom")):
        tall.alpha_composite(Image.open(root / f"map_{p}_{act}.png").convert("RGBA"), (0, 1440 * i))
    l, rgb = mean_l(tall)
    print(f"{act} map paper L={l:.1f} mean #{round(rgb[0]):02X}{round(rgb[1]):02X}{round(rgb[2]):02X} bbox={tall.getchannel('A').getbbox()}")
for name in ("rest_sites/glory_rest_site_00", "rest_sites/glory_rest_site_l_log", "rest_sites/glory_rest_site_fire",
             "rest_sites/glory_rest_site_cutter_1"):
    img = Image.open(REF / f"{name}.png")
    img = img.convert("RGBA") if "A" in img.getbands() else img.convert("RGB")
    print(name, "L=%.1f" % mean_l(img, 128)[0])
