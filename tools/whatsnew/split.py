"""Makes 바뀐 점's files from CHANGELOG.md: python tools/whatsnew/split.py

Bloom fetches them from GitHub (Core/ReleaseNotes.cs): docs/whatsnew/versions.txt lists the versions, newest first, and
docs/whatsnew/<version>.md holds each one's section (English, then "### 한국어"). Run it after writing a release's
section, before the push; a test checks that the files match the changelog.
"""
import os
import re

root = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')
out = os.path.join(root, 'docs', 'whatsnew')

with open(os.path.join(root, 'CHANGELOG.md'), encoding='utf-8') as f:
    text = f.read().replace('\r\n', '\n')

sections = []  # (version, body)
for part in re.split(r'^## ', text, flags=re.M)[1:]:
    version, _, body = part.partition('\n')
    version = version.strip()
    if version:
        sections.append((version, body.strip() + '\n'))

os.makedirs(out, exist_ok=True)
keep = {'versions.txt'}
with open(os.path.join(out, 'versions.txt'), 'w', encoding='utf-8', newline='\n') as f:
    f.write(''.join(v + '\n' for v, _ in sections))
for version, body in sections:
    name = version + '.md'
    keep.add(name)
    with open(os.path.join(out, name), 'w', encoding='utf-8', newline='\n') as f:
        f.write(body)
for name in os.listdir(out):
    if name not in keep:
        os.remove(os.path.join(out, name))
        print('removed', name)
print(len(sections), 'versions ->', os.path.relpath(out, root))
