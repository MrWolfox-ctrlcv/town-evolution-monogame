"""User-approved direct download of the one missing Android platform; reuse all other SDK parts."""
import hashlib, json, pathlib, shutil, ssl, urllib.request, zipfile
root=pathlib.Path(__file__).resolve().parent.parent
sdk=root/'.tools/android-sdk'
metadata=json.loads((sdk/'platform-36-metadata.json').read_text(encoding='utf8'))
archive=root/'.tools/downloads/platform-36_r02.zip'
opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),urllib.request.HTTPSHandler(context=ssl.create_default_context()))
if not archive.exists():
    with opener.open(metadata['url'],timeout=40) as response,archive.open('wb') as output:
        shutil.copyfileobj(response,output,1024*1024)
if archive.stat().st_size!=metadata['bytes'] or hashlib.file_digest(archive.open('rb'),'sha1').hexdigest()!=metadata['sha1']:
    raise RuntimeError('Official Android package size/checksum mismatch; no extraction performed.')
platforms=sdk/'platforms';platforms.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(archive) as package:
    for member in package.infolist():
        if not (platforms/member.filename).resolve().is_relative_to(platforms.resolve()):raise RuntimeError('Unexpected archive path')
    package.extractall(platforms)
jar=next(platforms.rglob('android.jar'))
if jar.parent.name!='android-36':
    destination=platforms/'android-36'
    if not jar.parent.resolve().is_relative_to(platforms.resolve()) or not destination.resolve().is_relative_to(platforms.resolve()):raise RuntimeError('Invalid platform rename')
    jar.parent.rename(destination)
print('Android API 36 ready in project-local SDK. Direct HTTPS; official checksum verified.')
