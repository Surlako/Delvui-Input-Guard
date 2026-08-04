# GitHub setup

1. Create a new public GitHub repository, for example `DelvUI-Input-Guard`.
2. Upload every file from this package to the repository root.
3. If Windows hides `.github`, create this file directly on GitHub:

   `.github/workflows/build-release.yml`

   Then paste the workflow contents from this package.
4. Commit the files.
5. Open **Actions** → **Build and publish plugin** → **Run workflow**.
6. After a successful run, confirm that:
   - the latest release contains `latest.zip`;
   - `pluginmaster.json` exists in the repository root.
7. Add the raw `pluginmaster.json` URL to Dalamud custom plugin repositories.
