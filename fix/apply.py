"""Apply the reviewed fork patch; stop if upstream no longer matches."""
import argparse
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def apply(target: Path) -> None:
    repo_root = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"], cwd=target, check=True,
        capture_output=True, text=True,
    ).stdout.strip()
    if Path(repo_root).resolve() != target.resolve():
        raise RuntimeError("Target must be the root of a clean upstream Git checkout")
    patch = ROOT / "fix" / "remove-region-restriction.patch"
    subprocess.run(["git", "apply", "--check", str(patch)], cwd=target, check=True)
    subprocess.run(["git", "apply", str(patch)], cwd=target, check=True)
    policy = target / "PCL.Core/App/Region/RegionPolicy.cs"
    if "public bool IsRestricted => false;" not in policy.read_text(encoding="utf-8-sig"):
        raise RuntimeError("Region policy verification failed")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", type=Path)
    apply(parser.parse_args().target.resolve())
