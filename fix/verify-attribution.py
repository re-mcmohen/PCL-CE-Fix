"""Verify the original author remains first in About, alongside the sponsorship link."""
import sys
from pathlib import Path
import xml.etree.ElementTree as ET


def verify(root: Path) -> None:
    ns = {"pcl": "clr-namespace:PCL"}
    page = ET.parse(root / "Plain Craft Launcher 2/Pages/PageSetup/PageSetupAbout.xaml")
    card = page.find(".//pcl:MyCard", ns)
    if card is None:
        raise RuntimeError("About author card is missing")
    authors = card.findall(".//pcl:MyListItem", ns)
    if not authors or authors[0].get("Grid.Row") != "0" or \
            authors[0].get("Title") != "{DynamicResource Setup.About.OriginalAuthor.Name}":
        raise RuntimeError("The original author must remain first in About")
    sponsor = any(button.get("Grid.Row") == "0" and
                  button.get("{clr-namespace:PCL}CustomEventService.EventData") == "https://ifdian.net/a/LTCat"
                  for button in card.findall(".//pcl:MyButton", ns))
    if not sponsor:
        raise RuntimeError("The original author's sponsorship link is missing")
    locale = ET.parse(root / "PCL.Core/App/Localization/Languages/zh-CN.xaml")
    key = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"
    name = next((node.text for node in locale.getroot()
                 if node.get(key) == "Setup.About.OriginalAuthor.Name"), None)
    if name != "龙腾猫跃":
        raise RuntimeError("The original author attribution has changed")


if __name__ == "__main__":
    verify(Path(sys.argv[1]).resolve())
    print("PASS: original author and sponsorship remain first in About")
