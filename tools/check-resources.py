#!/usr/bin/env python3
"""Checks the things the compiler cannot.

The front end looks strings up by key at runtime, so a typo in a `data-i18n`
attribute or a `t('...')` call is invisible until someone opens that screen and
sees a blank label. This script runs anywhere and catches it, along with keys that
exist in one language but not the other.

    python3 tools/check-resources.py
"""

import glob
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WEB = os.path.join(ROOT, 'src', 'ReceiptCapture.Web')
CORE = os.path.join(ROOT, 'src', 'ReceiptCapture.Core')
RESX = os.path.join(CORE, 'Resources', 'Strings', 'AppResources.resx')
RESX_FR = os.path.join(CORE, 'Resources', 'Strings', 'AppResources.fr.resx')


def read(path):
    return io.open(path, encoding='utf-8').read()


def resx_keys(path):
    return {d.get('name') for d in ET.parse(path).getroot().findall('data')}


def main():
    failures = []

    english = resx_keys(RESX)
    french = resx_keys(RESX_FR)

    only_english = sorted(english - french)
    only_french = sorted(french - english)
    if only_english:
        failures.append('keys missing a French translation: %s' % only_english)
    if only_french:
        failures.append('French keys with no English original: %s' % only_french)

    used = set()

    # data-i18n="Key" in the markup
    for path in glob.glob(os.path.join(WEB, 'wwwroot', '**', '*.html'), recursive=True):
        used |= set(re.findall(r'data-i18n="([A-Za-z0-9_]+)"', read(path)))

    # t('Key') and t("Key") in the front-end script
    for path in glob.glob(os.path.join(WEB, 'wwwroot', '**', '*.js'), recursive=True):
        body = read(path)
        used |= set(re.findall(r"\bt\(\s*'([A-Za-z0-9_]+)'", body))
        used |= set(re.findall(r'\bt\(\s*"([A-Za-z0-9_]+)"', body))

    # AppStrings.Get("Key") / AppStrings.Format("Key", ...) on the server
    for path in (glob.glob(os.path.join(WEB, '**', '*.cs'), recursive=True)
                 + glob.glob(os.path.join(CORE, '**', '*.cs'), recursive=True)):
        if os.sep + 'obj' + os.sep in path or os.sep + 'bin' + os.sep in path:
            continue
        used |= set(re.findall(r'AppStrings\.(?:Get|Format)\("([A-Za-z0-9_]+)"', read(path)))

    missing = sorted(used - english)
    if missing:
        failures.append('resource keys referenced but not defined: %s' % missing)

    # Parser warnings reach the user as text, so every one needs a translation.
    warnings = set(re.findall(
        r'"(Warning_[A-Za-z]+)"',
        read(os.path.join(CORE, 'Parsing', 'ParseWarnings.cs'))))
    missing_warnings = sorted(warnings - english)
    if missing_warnings:
        failures.append('parser warnings with no resource: %s' % missing_warnings)

    # Every element the script hides must use the `hidden` attribute, which the
    # stylesheet forces to win; a stray style="display:none" would not survive
    # being shown again.
    css = read(os.path.join(WEB, 'wwwroot', 'styles.css'))
    if '[hidden]' not in css:
        failures.append('styles.css must keep the [hidden] override, or hidden views render on top of each other')

    print('%d resource keys referenced, %d defined in each language.' % (len(used), len(english)))

    if failures:
        print('\nFAILURES:')
        for failure in failures:
            print('  - ' + failure)
        return 1

    print('All resource references check out.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
