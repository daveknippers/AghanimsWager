const path = require('path');
const CopyWebpackPlugin = require('copy-webpack-plugin');

module.exports = {
  configureWebpack: {
    devServer: {
      https: true,
      headers: {
        "Access-Control-Allow-Origin": "*"
      },
      allowedHosts: "all",
      port: 8080,
      server: "localhost"
    },
    output: {
      libraryTarget: "system", 
      filename: "js/aghanims-wager-spa-vue-app.js"
    },
    plugins: [
      new CopyWebpackPlugin({
        patterns: [
          { from: 'src/assets/*', to: 'assets/[name][ext]'},
        ]
      })
    ]
  },
  filenameHashing: false
};
