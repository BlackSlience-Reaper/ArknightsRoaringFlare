"""裁切图片：crop.py <in> <out> x0 y0 x1 y1"""
import sys
from PIL import Image

src, dst, *box = sys.argv[1:]
Image.open(src).crop(tuple(int(v) for v in box)).save(dst)
