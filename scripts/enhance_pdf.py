import sys
import tempfile
from pathlib import Path

import fitz
from PIL import Image, ImageFilter, ImageOps


DEFAULT_DPI_SCALE = 2.0


def enhance_image(img: Image.Image) -> Image.Image:
    img = img.convert("RGB")
    img = ImageOps.autocontrast(img, cutoff=1)
    img = img.filter(ImageFilter.SHARPEN)
    img = img.filter(ImageFilter.UnsharpMask(radius=1.5, percent=180, threshold=3))
    return img


def enhance_pdf(input_pdf: Path, output_pdf: Path, dpi_scale: float = DEFAULT_DPI_SCALE) -> None:
    src = fitz.open(str(input_pdf))
    out = fitz.open()
    temp_files: list[str] = []

    try:
        total_pages = len(src)
        for index, page in enumerate(src, start=1):
            print(f"Processing page {index}/{total_pages}", flush=True)

            pix = page.get_pixmap(
                matrix=fitz.Matrix(dpi_scale, dpi_scale),
                alpha=False,
            )

            img = Image.frombytes("RGB", (pix.width, pix.height), pix.samples)
            img = enhance_image(img)

            with tempfile.NamedTemporaryFile(suffix=".jpg", delete=False) as tmp:
                img.save(tmp.name, "JPEG", quality=92)
                temp_files.append(tmp.name)
                image_path = tmp.name

            new_page = out.new_page(width=pix.width, height=pix.height)
            new_page.insert_image(
                fitz.Rect(0, 0, pix.width, pix.height),
                filename=image_path,
            )

        output_pdf.parent.mkdir(parents=True, exist_ok=True)
        out.save(
            str(output_pdf),
            garbage=4,
            deflate=True,
            clean=True,
        )
    finally:
        src.close()
        out.close()
        for temp_path in temp_files:
            try:
                Path(temp_path).unlink(missing_ok=True)
            except Exception:
                pass


def parse_args(argv: list[str]) -> tuple[Path, Path, float]:
    if len(argv) < 3:
        raise ValueError("Usage: enhance_pdf.py <input_pdf> <output_pdf> [dpi_scale]")

    input_pdf = Path(argv[1]).expanduser().resolve()
    output_pdf = Path(argv[2]).expanduser().resolve()
    dpi_scale = DEFAULT_DPI_SCALE

    if len(argv) >= 4:
        dpi_scale = float(argv[3])

    return input_pdf, output_pdf, dpi_scale


def main() -> int:
    try:
        input_pdf, output_pdf, dpi_scale = parse_args(sys.argv)
        if not input_pdf.exists():
            raise FileNotFoundError(f"Input PDF not found: {input_pdf}")

        enhance_pdf(input_pdf, output_pdf, dpi_scale)
        print(f"Done: {output_pdf}", flush=True)
        return 0
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr, flush=True)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
