"""Apply the reviewed fork patch; stop if upstream no longer matches."""
import argparse
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]

# These files implement the fork update UI/service, independently of the upstream updater.
OWNED_UPDATE_FILES = (
    "Plain Craft Launcher 2/Modules/Updates/UpdateManager.cs",
    "Plain Craft Launcher 2/Pages/PageSetup/PageSetupUpdate.xaml",
    "Plain Craft Launcher 2/Pages/PageSetup/PageSetupUpdate.xaml.cs",
)


def apply(target: Path) -> None:
    repo_root = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"], cwd=target, check=True,
        capture_output=True, text=True,
    ).stdout.strip()
    if Path(repo_root).resolve() != target.resolve():
        raise RuntimeError("Target must be the root of a clean upstream Git checkout")
    patches = [str(ROOT / "fix" / name) for name in
               ("remove-region-restriction.patch", "launcher-experience.patch")]
    for name in OWNED_UPDATE_FILES:
        if not (target / name).is_file() or not (ROOT / "fix" / "overlay" / name).is_file():
            raise RuntimeError(f"Owned update file missing or renamed; review upstream compatibility: {name}")
    subprocess.run(["git", "apply", "--check", *patches], cwd=target, check=True)
    subprocess.run(["git", "apply", *patches], cwd=target, check=True)
    for name in OWNED_UPDATE_FILES:
        shutil.copyfile(ROOT / "fix" / "overlay" / name, target / name)
    policy = target / "PCL.Core/App/Region/RegionPolicy.cs"
    if "public bool IsRestricted => false;" not in policy.read_text(encoding="utf-8-sig"):
        raise RuntimeError("Region policy verification failed")
    subprocess.run([sys.executable, str(ROOT / "fix" / "verify-attribution.py"), str(target)], check=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", type=Path)
    apply(parser.parse_args().target.resolve())
