# Configuration file for the Sphinx documentation builder.

# -- Project information

project = 'Desomnia'
copyright = '2026, mad0x20wizard'
author = 'mad0x20wizard'

# Keep the displayed version aligned with the checked-out release.
# Source archives without git retain the latest documented feature release.
import subprocess
from pathlib import Path

try:
    release = subprocess.check_output(
        ['git', 'describe', '--tags', '--abbrev=0'],
        cwd=Path(__file__).resolve().parent.parent,
        stderr=subprocess.DEVNULL, text=True,
    ).strip().removeprefix('v')
except (OSError, subprocess.CalledProcessError):
    release = '3.3.0'
version = '.'.join(release.split('.')[:2])

# -- General configuration

extensions = [
    'sphinx.ext.duration',
    'sphinx.ext.doctest',
    'sphinx.ext.autodoc',
    'sphinx.ext.autosummary',
    'sphinx.ext.intersphinx',
    
    'sphinx_rtd_dark_mode'
]

intersphinx_mapping = {
    'python': ('https://docs.python.org/3/', None),
    'sphinx': ('https://www.sphinx-doc.org/en/master/', None),
}
intersphinx_disabled_domains = ['std']

templates_path = ['_templates']

# -- Options for HTML output

html_theme = 'sphinx_rtd_theme'

# -- Options for EPUB output
epub_show_urls = 'footnote'

html_static_path = ['_static']

default_dark_mode = False

def setup(app):
    app.add_css_file('css/custom.css')   # Sphinx 1.8+
    # app.add_js_file('custom.js')   # if you want JS too
