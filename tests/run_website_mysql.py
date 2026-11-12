"""Disposable schema on the separate local clean Docker database; no live credentials."""
import os,subprocess,uuid
from pathlib import Path
schema='cw_test_skins3d_'+uuid.uuid4().hex[:10]
sql=['docker','exec','-e','MYSQL_PWD=root','gamesite-skins3d-graffiti-clean-db-1','mysql','-uroot','-e']
subprocess.run(sql+['CREATE DATABASE `'+schema+'`'],check=True)
try:
    env={**os.environ,'CW_TEST_MYSQL':f'Server=127.0.0.1;Port=3320;User ID=root;Password=root;Database={schema};SSL Mode=Disabled'}
    subprocess.run(['dotnet','test','CustomWeapons.sln','-c','Release','--no-restore'],cwd=Path(__file__).resolve().parents[1],env=env,check=True)
finally:subprocess.run(sql+['DROP DATABASE `'+schema+'`'],check=True)
