<template>
  <q-layout view="hHh lpR fFf" id="app">

    <q-header elevated class="bg-primary text-white" height-hint="98">
      <q-toolbar>
        <q-toolbar-title>
          <q-avatar>
            <img src="https://cdn.quasar.dev/logo-v2/svg/logo-mono-white.svg">
          </q-avatar>
          Title
        </q-toolbar-title>
      </q-toolbar>

      <q-tabs align="left">
        <q-route-tab to="/about" label="About" />
        <q-route-tab to="/players" label="Players" />
        <q-route-tab to="/matches" label="Matches" />
      </q-tabs>
    </q-header>

    <q-page-container>
      <router-view />
    </q-page-container>

  </q-layout>
</template>

<script lang="ts">
import { defineComponent } from 'vue';

export default defineComponent({
  name: 'App'
});
</script>

<style>
#app {
  font-family: Avenir, Helvetica, Arial, sans-serif;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
  text-align: center;
  color: #2c3e50;
  margin-top: 60px;
}

.nav-container {
  margin-left: 10px;
}

/*-------------------- Body --------------------*/
*,
*::before,
*::after {
  box-sizing: border-box;
}

body {
  margin: 0;
  background: radial-gradient(ellipse farthest-corner at center top, #6492f3 0%, #416de7 100%);
  color: #fff;
}
</style>