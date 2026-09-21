#!/usr/bin/env python3
"""Checks the things the compiler cannot.

The iOS head only builds on a Mac, and XAML resource lookups fail at runtime
rather than at build time. This script runs anywhere and catches the common
breakages: malformed XAML, a {loc:Translate Key} with no matching resource, a
{StaticResource Name} that was never defined, and a parser warning with no
translation.

    python3 tools/check-resources.py
"""

import glob
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, 'src', 'ReceiptCapture.App')
CORE = os.path.join(ROOT, 'src', 'ReceiptCapture.Core')
RESX = os.path.join(CORE, 'Resources', 'Strings', 'AppResources.resx')
RESX_FR = os.path.join(CORE, 'Resources', 'Strings', 'AppResources.fr.resx')


def read(path):
    return io.open(path, encoding='utf-8').read()


def resx_keys(path):
    return {d.get('name') for d in ET.parse(path).getroot().findall('data')}


def main():
    failures = []
    xamls = sorted(glob.glob(os.path.join(APP, '**', '*.xaml'), recursive=True))

    for path in xamls:
        try:
            ET.parse(path)
        except ET.ParseError as error:
            failures.append('%s: malformed XAML: %s' % (path, error))

    english = resx_keys(RESX)
    french = resx_keys(RESX_FR)

    only_english = sorted(english - french)
    only_french = sorted(french - english)
    if only_english:
        failures.append('keys missing a French translation: %s' % only_english)
    if only_french:
        failures.append('French keys with no English original: %s' % only_french)

    used = set()
    for path in xamls:
        used |= set(re.findall(r'\{loc:Translate\s+([A-Za-z0-9_]+)\s*\}', read(path)))
    for path in (glob.glob(os.path.join(APP, '**', '*.cs'), recursive=True)
                 + glob.glob(os.path.join(CORE, '**', '*.cs'), recursive=True)):
        used |= set(re.findall(r'AppStrings\.(?:Get|Format)\("([A-Za-z0-9_]+)"', read(path)))

    missing = sorted(used - english)
    if missing:
        failures.append('resource keys referenced but not defined: %s' % missing)

    defined = set()
    referenced = set()
    for path in xamls:
        body = read(path)
        defined |= set(re.findall(r'x:Key="([^"]+)"', body))
        referenced |= set(re.findall(r'\{StaticResource\s+([A-Za-z0-9_]+)\s*\}', body))

    undefined = sorted(referenced - defined)
    if undefined:
        failures.append('StaticResource used but never defined: %s' % undefined)

    warnings = set(re.findall(
        r'"(Warning_[A-Za-z]+)"',
        read(os.path.join(CORE, 'Parsing', 'ParseWarnings.cs'))))
    missing_warnings = sorted(warnings - english)
    if missing_warnings:
        failures.append('parser warnings with no resource: %s' % missing_warnings)

    print('%d XAML files, %d resource keys, %d StaticResource references checked.'
          % (len(xamls), len(used), len(referenced)))

    if failures:
        print('\nFAILURES:')
        for failure in failures:
            print('  - ' + failure)
        return 1

    print('All XAML and resource references check out.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
