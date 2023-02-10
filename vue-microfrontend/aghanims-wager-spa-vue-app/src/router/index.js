import Vue from "vue";
import VueRouter from "vue-router";
import Home from "../components/Home.vue";
import About from "../components/About.vue";
import Matches from "../components/Matches.vue";
import Players from "../components/Players.vue";

Vue.use(VueRouter);

const routes = [
    { path: "/home", component: Home },
    { path: "/about", component: About },
    { path: "/players", component: Players },
    { path: "/matches", component: Matches },
];

const router = new VueRouter({
  mode: "history",
  base: process.env.BASE_URL,
  routes
});

export default router;
