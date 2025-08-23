#!/bin/bash
# Setup script for Perfetto submodule with sparse checkout
# This ensures the submodule only contains the protos/ directory (3.7MB instead of 141MB)

set -e

echo "Setting up Perfetto submodule with sparse checkout..."

# Navigate to the project root
cd "$(dirname "$0")/.."

# Initialize and update submodule if not already done
if [ ! -d "Perfetto.Protos/perfetto-submodule/.git" ]; then
    echo "Initializing submodule..."
    git submodule init Perfetto.Protos/perfetto-submodule
    git submodule update Perfetto.Protos/perfetto-submodule
fi

# Configure sparse checkout
echo "Configuring sparse checkout to only include protos/ directory..."
cd Perfetto.Protos/perfetto-submodule

# Enable sparse checkout
git config core.sparseCheckout true

# Set sparse checkout pattern - we only need the protos directory
mkdir -p ../../.git/modules/Perfetto.Protos/perfetto-submodule/info
echo "protos/*" > ../../.git/modules/Perfetto.Protos/perfetto-submodule/info/sparse-checkout

# Apply sparse checkout
git read-tree -m -u HEAD

echo "Sparse checkout configured successfully!"
echo "Submodule size: $(du -sh . | cut -f1)"
echo "Only protos/ directory is checked out instead of the full 141MB repository."